using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DeepseekPet.Bridge
{
    /// <summary>
    /// Claude Code 钩子负载 → 统一事件。
    ///
    /// 钩子本身不带 token 用量，所以「生成」要从 transcript（负载里的 transcript_path，
    /// 一个 JSONL）读：每条 assistant 消息带 message.usage。Claude 把同一条消息按内容块
    /// 拆成多行写，每行重复同一份（最终的）usage —— 必须按 message.id 去重，否则一条
    /// 回复会被算两三次（实测 151 条消息里 112 条是多行）。
    ///
    /// 第一次见到一个 transcript 时从末尾开始读：之前的历史不该回放。
    /// 线程安全：HTTP 请求可能并发进来，transcript 状态整体加锁。
    /// </summary>
    public sealed class ClaudeCodeAdapter
    {
        /// <summary>cache_read 算不算进喂食量。默认不算：长对话每一步都读十几万缓存，算进去顿顿都是大餐。</summary>
        public bool CountCacheReads;

        readonly object _lock = new object();
        readonly Dictionary<string, TranscriptState> _transcripts = new Dictionary<string, TranscriptState>(StringComparer.OrdinalIgnoreCase);
        long _seq;

        sealed class TranscriptState
        {
            public JsonlTail Tail;
            public readonly HashSet<string> Seen = new HashSet<string>();
            public readonly Queue<string> SeenOrder = new Queue<string>();
        }

        /// <summary>处理一个钩子负载。返回 0 到多条事件（工具结果之后可能顺带读出几条生成）。</summary>
        public List<PetEvent> Handle(JObject payload, long now)
        {
            var events = new List<PetEvent>();
            if (payload == null) return events;
            var hook = payload.Value<string>("hook_event_name") ?? "";
            var session = payload.Value<string>("session_id") ?? "";
            var transcript = payload.Value<string>("transcript_path");

            // 先读 transcript：工具结果到之前，发起这次调用的那条 assistant 消息已经落盘了，
            // 先报生成再报结果，顺序和 dsh 里一致。
            if (!string.IsNullOrEmpty(transcript)) events.AddRange(ReadTranscript(transcript, session));

            var seq = System.Threading.Interlocked.Increment(ref _seq);
            switch (hook)
            {
                case "UserPromptSubmit":
                {
                    var prompt = payload.Value<string>("prompt") ?? "";
                    events.Add(Make(session, PetEventKind.UserInput, now, "prompt:" + now + ":" + seq, e => e.Tokens = TokenMath.UserInputTokens(prompt)));
                    events.Add(Make(session, PetEventKind.TurnStart, now, "turn:" + now + ":" + seq, null));
                    break;
                }
                case "PreToolUse":
                {
                    var raw = payload.Value<string>("tool_name") ?? "";
                    var callId = payload.Value<string>("tool_use_id") ?? "";
                    events.Add(Make(session, PetEventKind.ToolCall, now, "call:" + (callId != "" ? callId : now + ":" + seq), e =>
                    {
                        e.RawTool = raw;
                        e.Tool = ToolNames.Canonical(PetSource.ClaudeCode, raw);
                        e.CallId = callId;
                        e.File = ToolNames.FileArgOf(payload["tool_input"]);
                    }));
                    break;
                }
                case "PostToolUse":
                case "PostToolUseFailure":
                {
                    var raw = payload.Value<string>("tool_name") ?? "";
                    var callId = payload.Value<string>("tool_use_id") ?? "";
                    var failed = hook == "PostToolUseFailure";
                    var body = failed ? payload["error"] : payload["tool_response"];
                    events.Add(Make(session, PetEventKind.ToolResult, now, "result:" + (callId != "" ? callId : now + ":" + seq), e =>
                    {
                        e.RawTool = raw;
                        e.Tool = ToolNames.Canonical(PetSource.ClaudeCode, raw);
                        e.CallId = callId;
                        e.Failed = failed;
                        e.Tokens = TokenMath.ToolResultTokens(Serialize(body));
                    }));
                    break;
                }
                case "Stop":
                case "SubagentStop":
                    events.Add(Make(session, PetEventKind.TurnEnd, now, "stop:" + now + ":" + seq, null));
                    break;
                case "SessionStart":
                    events.Add(Make(session, PetEventKind.SessionStart, now, "session-start:" + now + ":" + seq, null));
                    break;
                case "SessionEnd":
                    events.Add(Make(session, PetEventKind.SessionEnd, now, "session-end:" + now + ":" + seq, null));
                    lock (_lock)
                    {
                        if (!string.IsNullOrEmpty(transcript)) _transcripts.Remove(transcript);
                    }
                    break;
            }
            return events;
        }

        /// <summary>读一个 transcript 自上次以来新增的 assistant 消息，每条（按 message.id）报一次生成。</summary>
        public List<PetEvent> ReadTranscript(string path, string session)
        {
            var events = new List<PetEvent>();
            lock (_lock)
            {
                if (!_transcripts.TryGetValue(path, out var state))
                {
                    state = new TranscriptState { Tail = new JsonlTail(path, fromEnd: true) };
                    _transcripts[path] = state;
                    return events;
                }
                foreach (var line in state.Tail.ReadNewLines())
                {
                    var e = ParseTranscriptLine(line, session, state);
                    if (e != null) events.Add(e);
                }
            }
            return events;
        }

        PetEvent ParseTranscriptLine(string line, string session, TranscriptState state)
        {
            JObject entry;
            try
            {
                entry = JObject.Parse(line);
            }
            catch (JsonException)
            {
                return null;
            }
            if (entry.Value<string>("type") != "assistant") return null;
            if (!(entry["message"] is JObject message)) return null;
            if (!(message["usage"] is JObject usage)) return null;
            var id = message.Value<string>("id");
            if (string.IsNullOrEmpty(id)) return null;
            if (!Remember(state, id)) return null;

            var output = usage.Value<long?>("output_tokens") ?? 0;
            var input = usage.Value<long?>("input_tokens") ?? 0;
            var cacheWrite = usage.Value<long?>("cache_creation_input_tokens") ?? 0;
            var cacheRead = CountCacheReads ? usage.Value<long?>("cache_read_input_tokens") ?? 0 : 0;
            var ts = ParseIsoMs(entry.Value<string>("timestamp"));
            var sid = entry.Value<string>("sessionId") ?? session;
            return new PetEvent
            {
                Id = PetSource.ClaudeCode + ":" + sid + ":msg:" + id,
                Source = PetSource.ClaudeCode,
                Session = sid,
                Kind = PetEventKind.Generation,
                Ts = ts,
                Tokens = output + input + cacheWrite + cacheRead,
                Output = Math.Max(0, output),
            };
        }

        /// <summary>记下一个 message.id；已经见过就返回 false。只留最近 1024 个，够覆盖一个文件的追读窗口。</summary>
        static bool Remember(TranscriptState state, string id)
        {
            if (state.Seen.Contains(id)) return false;
            state.Seen.Add(id);
            state.SeenOrder.Enqueue(id);
            while (state.SeenOrder.Count > 1024) state.Seen.Remove(state.SeenOrder.Dequeue());
            return true;
        }

        static PetEvent Make(string session, string kind, long now, string unique, Action<PetEvent> fill)
        {
            var e = new PetEvent
            {
                Id = PetSource.ClaudeCode + ":" + session + ":" + unique,
                Source = PetSource.ClaudeCode,
                Session = session,
                Kind = kind,
                Ts = now,
            };
            fill?.Invoke(e);
            return e;
        }

        static string Serialize(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return "";
            if (token.Type == JTokenType.String) return token.Value<string>();
            return token.ToString(Formatting.None);
        }

        /// <summary>ISO 时间 → epoch 毫秒；解析不了就当作「现在」（交给闸门的是发生时刻，宁可新鲜也别丢）。</summary>
        internal static long ParseIsoMs(string iso)
        {
            if (!string.IsNullOrEmpty(iso)
                && DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t))
            {
                return t.ToUnixTimeMilliseconds();
            }
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
}
