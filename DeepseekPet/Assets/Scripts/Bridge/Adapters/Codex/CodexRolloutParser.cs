using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DeepseekPet.Bridge
{
    /// <summary>
    /// 一个 Codex rollout 文件（~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl）的逐行解析。
    /// 每个文件一个实例：要记住 call_id → 工具名（结果行里没有工具名）和上一次的累计 token。
    ///
    /// 对照 Codex CLI 0.163 的实录：
    ///   session_meta                         payload.id → 会话 id
    ///   event_msg/task_started|task_complete 回合开始 / 结束（被打断时是 turn_aborted）
    ///   event_msg/item_completed:UserMessage 用户输入（content 里的 text 块）
    ///   response_item/function_call          工具调用（name, arguments JSON 串, call_id）
    ///   response_item/custom_tool_call       同上，较新的 Codex 把 apply_patch 发成这种（input 是补丁文本）
    ///   response_item/*_call_output          工具结果；输出里的 "exited with code N" 判成败
    ///   event_msg/token_count                info.last_token_usage = 这一次请求的用量
    /// 认不出的行一律忽略：Codex 的格式还在变，宁可少喂也别崩。
    /// </summary>
    public sealed class CodexRolloutParser
    {
        static readonly Regex ExitCode = new Regex(@"(?:exited with code|""exit_code""\s*:)\s*(-?\d+)", RegexOptions.Compiled);

        readonly string _fileKey;
        string _session;
        readonly Dictionary<string, string> _callTools = new Dictionary<string, string>();
        long _lastTotalTokens = -1;
        long _lineNo;

        /// <param name="fileKey">文件的稳定标识（文件名），会话 id 还没读到时拿它兜底。</param>
        public CodexRolloutParser(string fileKey)
        {
            _fileKey = fileKey;
            _session = fileKey;
        }

        public List<PetEvent> Parse(string line)
        {
            var events = new List<PetEvent>();
            _lineNo++;
            JObject entry;
            try
            {
                entry = JObject.Parse(line);
            }
            catch (JsonException)
            {
                return events;
            }
            var type = entry.Value<string>("type");
            if (!(entry["payload"] is JObject payload)) return events;
            var ts = ClaudeCodeAdapter.ParseIsoMs(entry.Value<string>("timestamp"));
            var ptype = payload.Value<string>("type");

            if (type == "session_meta")
            {
                var id = payload.Value<string>("id") ?? payload.Value<string>("session_id");
                if (!string.IsNullOrEmpty(id)) _session = id;
                return events;
            }

            if (type == "event_msg")
            {
                switch (ptype)
                {
                    case "task_started":
                        events.Add(Make(PetEventKind.TurnStart, ts, "turn-start:" + _lineNo, null));
                        break;
                    case "task_complete":
                    // 用户中途打断（Esc）时没有 task_complete，只有这一条；回合同样结束了。
                    case "turn_aborted":
                        events.Add(Make(PetEventKind.TurnEnd, ts, "turn-end:" + _lineNo, null));
                        break;
                    case "item_completed":
                        if (payload["item"] is JObject item && item.Value<string>("type") == "UserMessage")
                        {
                            var text = TextOf(item["content"]);
                            var itemId = item.Value<string>("id") ?? _lineNo.ToString();
                            events.Add(Make(PetEventKind.UserInput, ts, "user:" + itemId, e => e.Tokens = TokenMath.UserInputTokens(text)));
                        }
                        break;
                    case "token_count":
                    {
                        var info = payload["info"] as JObject;
                        var total = info?["total_token_usage"]?.Value<long?>("total_tokens");
                        if (!(info?["last_token_usage"] is JObject last)) break;
                        // Codex 会把同一份用量重复报好几次：累计总量没变就不是一次新的请求。
                        if (total.HasValue)
                        {
                            if (total.Value == _lastTotalTokens) break;
                            _lastTotalTokens = total.Value;
                        }
                        var input = last.Value<long?>("input_tokens") ?? 0;
                        var cached = last.Value<long?>("cached_input_tokens") ?? 0;
                        var output = last.Value<long?>("output_tokens") ?? 0;
                        events.Add(Make(PetEventKind.Generation, ts, "tokens:" + (total ?? _lineNo), e =>
                        {
                            e.Tokens = Math.Max(0, input - cached) + output;
                            e.Output = Math.Max(0, output);
                        }));
                        break;
                    }
                }
                return events;
            }

            if (type == "response_item")
            {
                switch (ptype)
                {
                    case "function_call":
                    case "custom_tool_call":
                    case "local_shell_call":
                    {
                        var raw = payload.Value<string>("name") ?? (ptype == "local_shell_call" ? "local_shell" : "");
                        var callId = payload.Value<string>("call_id") ?? "";
                        if (callId != "") _callTools[callId] = raw;
                        var args = ptype == "custom_tool_call" ? payload.Value<string>("input") : payload.Value<string>("arguments");
                        events.Add(Make(PetEventKind.ToolCall, ts, "call:" + (callId != "" ? callId : _lineNo.ToString()), e =>
                        {
                            e.RawTool = raw;
                            e.Tool = ToolNames.Canonical(PetSource.Codex, raw);
                            e.CallId = callId;
                            e.File = raw == "apply_patch" ? ToolNames.FileOfPatch(args ?? "") ?? ToolNames.FileOfPatch(PatchInArgs(args)) : ToolNames.FileArgOf(args);
                        }));
                        break;
                    }
                    case "function_call_output":
                    case "custom_tool_call_output":
                    case "local_shell_call_output":
                    {
                        var callId = payload.Value<string>("call_id") ?? "";
                        _callTools.TryGetValue(callId, out var raw);
                        var output = OutputText(payload["output"]);
                        var m = ExitCode.Match(output);
                        var failed = m.Success && m.Groups[1].Value != "0";
                        events.Add(Make(PetEventKind.ToolResult, ts, "result:" + (callId != "" ? callId : _lineNo.ToString()), e =>
                        {
                            e.RawTool = raw;
                            e.Tool = raw == null ? null : ToolNames.Canonical(PetSource.Codex, raw);
                            e.CallId = callId;
                            e.Failed = failed;
                            e.Tokens = TokenMath.ToolResultTokens(output);
                        }));
                        break;
                    }
                }
            }
            return events;
        }

        PetEvent Make(string kind, long ts, string unique, Action<PetEvent> fill)
        {
            var e = new PetEvent
            {
                Id = PetSource.Codex + ":" + _session + ":" + unique,
                Source = PetSource.Codex,
                Session = _session,
                Kind = kind,
                Ts = ts,
            };
            fill?.Invoke(e);
            return e;
        }

        static string TextOf(JToken content)
        {
            if (!(content is JArray blocks)) return "";
            var text = "";
            foreach (var block in blocks)
            {
                if (block is JObject b && b["text"] != null && b["text"].Type == JTokenType.String) text += b.Value<string>("text");
            }
            return text;
        }

        /// <summary>工具输出可能是字符串，也可能是 { output, metadata } 这样的对象。</summary>
        static string OutputText(JToken output)
        {
            if (output == null || output.Type == JTokenType.Null) return "";
            if (output.Type == JTokenType.String) return output.Value<string>();
            return output.ToString(Formatting.None);
        }

        /// <summary>function_call 版的 apply_patch：补丁在 arguments JSON 的 input / patch 字段里。</summary>
        static string PatchInArgs(string args)
        {
            if (string.IsNullOrEmpty(args)) return "";
            try
            {
                var obj = JObject.Parse(args);
                return obj.Value<string>("input") ?? obj.Value<string>("patch") ?? "";
            }
            catch (JsonException)
            {
                return "";
            }
        }
    }
}
