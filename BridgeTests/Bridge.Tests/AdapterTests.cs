using System;
using System.IO;
using System.Linq;
using System.Text;
using DeepseekPet.Bridge;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DeepseekPet.Bridge.Tests
{
    public class ClaudeCodeAdapterTests
    {
        string _dir;
        string _transcript;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pet-bridge-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _transcript = Path.Combine(_dir, "session.jsonl");
            // 已有的历史：第一次见到这个文件时应当跳过。
            File.WriteAllText(_transcript, AssistantLine("old", 999, "thinking") + "\n");
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        /// <summary>和真实 transcript 同构的一行 assistant 记录（每个内容块一行，usage 相同）。</summary>
        static string AssistantLine(string id, long output, string block)
        {
            return new JObject
            {
                ["type"] = "assistant",
                ["sessionId"] = "s1",
                ["timestamp"] = DateTimeOffset.UtcNow.ToString("o"),
                ["message"] = new JObject
                {
                    ["id"] = id,
                    ["content"] = new JArray(new JObject { ["type"] = block }),
                    ["usage"] = new JObject
                    {
                        ["input_tokens"] = 4,
                        ["cache_creation_input_tokens"] = 100,
                        ["cache_read_input_tokens"] = 200000,
                        ["output_tokens"] = output,
                    },
                },
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        JObject Hook(string name, object extra = null)
        {
            var payload = new JObject
            {
                ["hook_event_name"] = name,
                ["session_id"] = "s1",
                ["transcript_path"] = _transcript,
            };
            if (extra != null) payload.Merge(JObject.FromObject(extra));
            return payload;
        }

        [Test]
        public void PromptMapsToUserInputAndPrimesTranscriptAtEnd()
        {
            var adapter = new ClaudeCodeAdapter();
            var events = adapter.Handle(Hook("UserPromptSubmit", new { prompt = new string('字', 400) }), 1000);
            Assert.AreEqual(new[] { PetEventKind.UserInput, PetEventKind.TurnStart }, events.Select(e => e.Kind).ToArray());
            Assert.AreEqual(200, events[0].Tokens);
            Assert.AreEqual(PetSource.ClaudeCode, events[0].Source);
            Assert.AreEqual("s1", events[0].Session);
        }

        [Test]
        public void DedupesMultiLineMessagesByMessageId()
        {
            var adapter = new ClaudeCodeAdapter();
            adapter.Handle(Hook("UserPromptSubmit", new { prompt = "hi" }), 1000);
            // 同一条消息拆成三行，外加写了一半的下一条。
            File.AppendAllText(_transcript,
                AssistantLine("m1", 50, "thinking") + "\n"
                + AssistantLine("m1", 50, "text") + "\n"
                + AssistantLine("m1", 50, "tool_use") + "\n"
                + AssistantLine("m2", 7, "text").Substring(0, 30));
            var events = adapter.Handle(Hook("PreToolUse", new { tool_name = "Edit", tool_use_id = "t1", tool_input = new { file_path = @"C:\x\store.js" } }), 2000);
            var gens = events.Where(e => e.Kind == PetEventKind.Generation).ToList();
            Assert.AreEqual(1, gens.Count, "同一个 message.id 只算一次");
            Assert.AreEqual(50 + 4 + 100, gens[0].Tokens, "默认 output + input + cache_creation，不算 cache_read");
            Assert.AreEqual(50, gens[0].Output);
            var call = events.Single(e => e.Kind == PetEventKind.ToolCall);
            Assert.AreEqual("edit", call.Tool);
            Assert.AreEqual("Edit", call.RawTool);
            Assert.AreEqual("store.js", call.File);
            Assert.AreEqual("t1", call.CallId);

            // 写完那半行之后才交出 m2。
            File.AppendAllText(_transcript, AssistantLine("m2", 7, "text").Substring(30) + "\n");
            var later = adapter.Handle(Hook("Stop"), 3000);
            Assert.AreEqual(new[] { PetEventKind.Generation, PetEventKind.TurnEnd }, later.Select(e => e.Kind).ToArray());
            Assert.AreEqual(7 + 4 + 100, later[0].Tokens);
        }

        [Test]
        public void CacheReadsCountWhenEnabled()
        {
            var adapter = new ClaudeCodeAdapter { CountCacheReads = true };
            adapter.Handle(Hook("UserPromptSubmit", new { prompt = "hi" }), 1000);
            File.AppendAllText(_transcript, AssistantLine("m1", 50, "text") + "\n");
            var gen = adapter.Handle(Hook("Stop"), 2000).Single(e => e.Kind == PetEventKind.Generation);
            Assert.AreEqual(50 + 4 + 100 + 200000, gen.Tokens);
        }

        [Test]
        public void ToolResultsCarryFailureAndSize()
        {
            var adapter = new ClaudeCodeAdapter();
            var ok = adapter.Handle(Hook("PostToolUse", new { tool_name = "Bash", tool_use_id = "t1", tool_response = new { stdout = new string('x', 98) } }), 1000)
                .Single(e => e.Kind == PetEventKind.ToolResult);
            Assert.IsFalse(ok.Failed);
            Assert.AreEqual("bash", ok.Tool);
            // {"stdout":"xxx…"} = 98 + 13 = 111 字节 → 56。
            Assert.AreEqual(56, ok.Tokens);

            var bad = adapter.Handle(Hook("PostToolUseFailure", new { tool_name = "Bash", tool_use_id = "t2", error = "exit 1" }), 1000)
                .Single(e => e.Kind == PetEventKind.ToolResult);
            Assert.IsTrue(bad.Failed);
            Assert.AreEqual("t2", bad.CallId);
        }
    }

    public class CodexTests
    {
        static string Line(string type, object payload, DateTimeOffset? at = null)
        {
            return new JObject
            {
                ["timestamp"] = (at ?? DateTimeOffset.UtcNow).ToString("o"),
                ["type"] = type,
                ["payload"] = JObject.FromObject(payload),
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        static string[] Session(string id)
        {
            return new[]
            {
                Line("session_meta", new { id, cwd = "C:\\x" }),
                Line("event_msg", new { type = "task_started" }),
                Line("event_msg", new { type = "item_completed", item = new { type = "UserMessage", id = "u1", content = new[] { new { type = "text", text = "你好你好" } } } }),
                Line("response_item", new { type = "function_call", name = "exec_command", arguments = "{\"cmd\":\"ls\"}", call_id = "c1" }),
                Line("response_item", new { type = "function_call_output", call_id = "c1", output = "Chunk ID: 1\nProcess exited with code 2\nOutput:\nboom" }),
                Line("response_item", new { type = "custom_tool_call", name = "apply_patch", input = "*** Begin Patch\n*** Update File: src/a.rs\n*** End Patch", call_id = "c2" }),
                Line("response_item", new { type = "custom_tool_call_output", call_id = "c2", output = "{\"output\":\"Success\",\"metadata\":{\"exit_code\":0}}" }),
                Line("event_msg", new { type = "token_count", info = new { total_token_usage = new { total_tokens = 10259 }, last_token_usage = new { input_tokens = 10240, cached_input_tokens = 240, output_tokens = 19 } } }),
                // Codex 会把同一份用量再报一次。
                Line("event_msg", new { type = "token_count", info = new { total_token_usage = new { total_tokens = 10259 }, last_token_usage = new { input_tokens = 10240, cached_input_tokens = 240, output_tokens = 19 } } }),
                Line("event_msg", new { type = "task_complete", last_agent_message = "done" }),
                "{ not json",
                Line("world_state", new { full = true }),
            };
        }

        [Test]
        public void ParsesARolloutIntoNormalizedEvents()
        {
            var parser = new CodexRolloutParser("rollout-x");
            var events = Session("sess-1").SelectMany(parser.Parse).ToList();
            Assert.AreEqual(new[]
            {
                PetEventKind.TurnStart, PetEventKind.UserInput, PetEventKind.ToolCall, PetEventKind.ToolResult,
                PetEventKind.ToolCall, PetEventKind.ToolResult, PetEventKind.Generation, PetEventKind.TurnEnd,
            }, events.Select(e => e.Kind).ToArray());
            Assert.IsTrue(events.All(e => e.Session == "sess-1" && e.Source == PetSource.Codex));
            Assert.AreEqual(2, events[1].Tokens);
            Assert.AreEqual("bash", events[2].Tool);
            Assert.IsTrue(events[3].Failed, "退出码 2 是失败");
            Assert.AreEqual("bash", events[3].Tool, "结果行没有工具名，按 call_id 回查");
            Assert.AreEqual("edit", events[4].Tool);
            Assert.AreEqual("a.rs", events[4].File);
            Assert.IsFalse(events[5].Failed, "exit_code 0 不是失败");
            Assert.AreEqual(10000 + 19, events[6].Tokens, "input - cached + output");
            Assert.AreEqual(19, events[6].Output);
            Assert.AreEqual(events.Count, events.Select(e => e.Id).Distinct().Count(), "id 各不相同");
        }

        [Test]
        public void InterruptedTurnStillEnds()
        {
            var parser = new CodexRolloutParser("rollout-x");
            var events = parser.Parse(Line("event_msg", new { type = "turn_aborted", turn_id = "t1", reason = "interrupted" }));
            Assert.AreEqual(PetEventKind.TurnEnd, events.Single().Kind, "打断的回合也算结束");
        }

        [Test]
        public void WatcherSkipsExistingContentAndReadsNewFilesFromStart()
        {
            var root = Path.Combine(Path.GetTempPath(), "pet-codex-test-" + Guid.NewGuid().ToString("N"));
            var now = DateTime.Now;
            var day = Path.Combine(root, now.ToString("yyyy"), now.ToString("MM"), now.ToString("dd"));
            Directory.CreateDirectory(day);
            try
            {
                var existing = Path.Combine(day, "rollout-old.jsonl");
                File.WriteAllLines(existing, Session("old"));
                var watcher = new CodexWatcher(root);
                Assert.AreEqual(0, watcher.Poll(now).Count, "启动时已有的内容不回放");

                File.AppendAllText(existing, Line("event_msg", new { type = "task_started" }) + "\n");
                var appended = watcher.Poll(now);
                Assert.AreEqual(1, appended.Count);
                Assert.AreEqual(PetEventKind.TurnStart, appended[0].Kind);

                File.WriteAllText(Path.Combine(day, "rollout-new.jsonl"), string.Join("\n", Session("new")) + "\n");
                var fresh = watcher.Poll(now);
                Assert.AreEqual(8, fresh.Count, "启动之后新出现的会话从头读");
                Assert.IsTrue(fresh.All(e => e.Session == "new"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void ResumedOldSessionIsPickedUpEvenWhenMtimeIsStale()
        {
            // 真机复现：续接三天前的会话，codex.exe 开着文件往里追加，Windows 上修改时间不更新。
            var root = Path.Combine(Path.GetTempPath(), "pet-codex-test-" + Guid.NewGuid().ToString("N"));
            var now = DateTime.Now;
            var old = now.AddDays(-3);
            var day = Path.Combine(root, old.ToString("yyyy"), old.ToString("MM"), old.ToString("dd"));
            Directory.CreateDirectory(day);
            try
            {
                var path = Path.Combine(day, "rollout-2026-10-19T10-00-00-01a15df6-72bc-7600-9d9d-63f4a97907a3.jsonl");
                File.WriteAllText(path, string.Join("\n", Session("ignored")) + "\n");
                var stale = old;
                File.SetLastWriteTime(path, stale);
                var watcher = new CodexWatcher(root);
                Assert.AreEqual(0, watcher.Poll(now).Count, "启动时只记长度");

                File.AppendAllText(path, Line("event_msg", new { type = "item_completed", item = new { type = "UserMessage", id = "u9", content = new[] { new { type = "text", text = "读取一下" } } } }) + "\n"
                    + Line("event_msg", new { type = "task_complete" }) + "\n");
                File.SetLastWriteTime(path, stale);

                var got = new System.Collections.Generic.List<PetEvent>();
                for (var i = 0; i < 4 && got.Count == 0; i++) got.AddRange(watcher.Poll(now));
                Assert.AreEqual(new[] { PetEventKind.UserInput, PetEventKind.TurnEnd }, got.Select(e => e.Kind).ToArray(), "按长度发现续接的旧会话，只读新增部分");
                Assert.AreEqual("01a15df6-72bc-7600-9d9d-63f4a97907a3", got[0].Session, "从中间开始读时，会话 id 从文件名里取");
                Assert.AreEqual(0, watcher.Poll(now).Count, "读过的不再读");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void RealLocalRolloutsParseWithoutErrors()
        {
            // 本机真实的 Codex 会话（只读）：格式漂移时这里先报警。没装 Codex 就跳过。
            var root = CodexWatcher.DefaultRoot();
            if (!Directory.Exists(root)) Assert.Ignore("本机没有 Codex 会话");
            var files = Directory.GetFiles(root, "rollout-*.jsonl", SearchOption.AllDirectories);
            if (files.Length == 0) Assert.Ignore("本机没有 Codex 会话");
            var kinds = 0;
            foreach (var file in files)
            {
                var parser = new CodexRolloutParser(Path.GetFileNameWithoutExtension(file));
                string text;
                using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8)) text = reader.ReadToEnd();
                foreach (var line in text.Split('\n'))
                {
                    if (line.Trim().Length == 0) continue;
                    kinds += parser.Parse(line).Count;
                }
            }
            Assert.Greater(kinds, 0, "真实会话里至少该认出一些事件");
        }
    }
}
