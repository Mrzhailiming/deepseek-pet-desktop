using DeepseekPet.Bridge;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DeepseekPet.Bridge.Tests
{
    public class TokenMathTests
    {
        [Test]
        public void UserInputIsCeilHalfOfUtf16Length()
        {
            // 和 JS 的 Math.ceil(text.length / 2) 一致。
            Assert.AreEqual(0, TokenMath.UserInputTokens(""));
            Assert.AreEqual(1, TokenMath.UserInputTokens("a"));
            Assert.AreEqual(2, TokenMath.UserInputTokens("abcd"));
            Assert.AreEqual(200, TokenMath.UserInputTokens(new string('字', 400)));
        }

        [Test]
        public void ToolResultIsCeilHalfOfUtf8BytesAtLeastOne()
        {
            Assert.AreEqual(1, TokenMath.ToolResultTokens(""));
            Assert.AreEqual(1, TokenMath.ToolResultTokens("a"));
            // 一个汉字 3 字节 → ceil(3/2) = 2。
            Assert.AreEqual(2, TokenMath.ToolResultTokens("字"));
            Assert.AreEqual(4000, TokenMath.ToolResultTokens(new string('x', 8000)));
        }
    }

    public class ToolNamesTests
    {
        [TestCase(PetSource.ClaudeCode, "Read", "read")]
        [TestCase(PetSource.ClaudeCode, "MultiEdit", "edit")]
        [TestCase(PetSource.ClaudeCode, "Task", "subagent")]
        [TestCase(PetSource.ClaudeCode, "mcp__idea__read_file", "mcp__idea__read_file")]
        [TestCase(PetSource.Codex, "exec_command", "bash")]
        [TestCase(PetSource.Codex, "apply_patch", "edit")]
        [TestCase(PetSource.Dsh, "Edit", "edit")]
        public void CanonicalNames(string source, string raw, string expected)
        {
            Assert.AreEqual(expected, ToolNames.Canonical(source, raw));
        }

        [Test]
        public void BaseNameHandlesBothSlashesAndTruncates()
        {
            Assert.AreEqual("store.js", ToolNames.BaseNameOf(@"C:\code\pet\lib\src\store.js"));
            Assert.AreEqual("store.js", ToolNames.BaseNameOf("/home/a/store.js/"));
            Assert.IsNull(ToolNames.BaseNameOf(""));
            Assert.AreEqual(40, ToolNames.BaseNameOf(new string('a', 60)).Length);
        }

        [Test]
        public void FileArgPrefersFilePath()
        {
            Assert.AreEqual("a.cs", ToolNames.FileArgOf(JObject.Parse("{\"file_path\":\"x/a.cs\",\"path\":\"y/b.cs\"}")));
            Assert.AreEqual("b.cs", ToolNames.FileArgOf("{\"path\":\"y/b.cs\"}"));
            Assert.IsNull(ToolNames.FileArgOf("{\"command\":\"ls\"}"));
            Assert.IsNull(ToolNames.FileArgOf("not json"));
        }

        [Test]
        public void FileOfPatchReadsFirstHeader()
        {
            var patch = "*** Begin Patch\n*** Update File: src/app/main.py\n@@\n-a\n+b\n*** End Patch";
            Assert.AreEqual("main.py", ToolNames.FileOfPatch(patch));
            Assert.IsNull(ToolNames.FileOfPatch("nothing here"));
        }
    }

    public class EventGateTests
    {
        static PetEvent At(string id, long ts) => new PetEvent { Id = id, Ts = ts, Kind = PetEventKind.UserInput };

        [Test]
        public void DropsDuplicatesAndStaleEvents()
        {
            var gate = new EventGate(30000);
            Assert.IsTrue(gate.Admit(At("a", 1000), 1000));
            Assert.IsFalse(gate.Admit(At("a", 1000), 1500), "同一个 id 只放一次");
            Assert.IsFalse(gate.Admit(At("old", 0), 31001), "超过 30s 的事件不放");
            Assert.IsTrue(gate.Admit(At("b", 30000), 31001));
        }

        [Test]
        public void RemembersAcrossOneGenerationSwap()
        {
            var gate = new EventGate(1000);
            Assert.IsTrue(gate.Admit(At("a", 0), 0));
            // 换代之后上一代还记着：窗口边缘重发的事件依然被挡住。
            Assert.IsTrue(gate.Admit(At("b", 1500), 1500));
            Assert.IsFalse(gate.Admit(At("a", 900), 1500));
        }
    }
}
