using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using DeepseekPet.Bridge;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DeepseekPet.Bridge.Tests
{
    public class ClaudeHookInstallerTests
    {
        static readonly BridgeConfig Config = new BridgeConfig { Port = 47823, Token = "tok" };

        const string Existing = @"{
  ""model"": ""opus"",
  ""hooks"": {
    ""PreToolUse"": [ { ""matcher"": ""Bash"", ""hooks"": [ { ""type"": ""command"", ""command"": ""echo mine"" } ] } ]
  }
}";

        [Test]
        public void InstallAddsOursAndKeepsEverythingElse()
        {
            var next = JObject.Parse(ClaudeHookInstaller.PreviewInstall(Existing, Config));
            Assert.AreEqual("opus", next.Value<string>("model"));
            var pre = (JArray)next["hooks"]["PreToolUse"];
            Assert.AreEqual(2, pre.Count, "用户自己的 PreToolUse 钩子还在");
            Assert.AreEqual("echo mine", pre[0]["hooks"][0].Value<string>("command"));
            var ours = pre[1]["hooks"][0];
            Assert.AreEqual("http", ours.Value<string>("type"));
            Assert.AreEqual("http://127.0.0.1:47823/hook/claude-code", ours.Value<string>("url"));
            Assert.AreEqual("tok", ours["headers"].Value<string>("X-Pet-Token"));
            Assert.AreEqual("*", pre[1].Value<string>("matcher"));
            Assert.IsNull(next["hooks"]["UserPromptSubmit"][0]["matcher"], "非工具事件不写 matcher");
            Assert.IsNull(next["hooks"]["SessionStart"], "SessionStart 不支持 http 钩子");
            foreach (var (name, _) in ClaudeHookInstaller.Events) Assert.IsNotNull(next["hooks"][name], name);
        }

        [Test]
        public void ReinstallIsIdempotentAndPicksUpNewPort()
        {
            var once = ClaudeHookInstaller.PreviewInstall(Existing, Config);
            var twice = ClaudeHookInstaller.PreviewInstall(once, Config);
            Assert.AreEqual(once, twice);
            var moved = JObject.Parse(ClaudeHookInstaller.PreviewInstall(once, new BridgeConfig { Port = 50000, Token = "t2" }));
            var urls = moved.SelectTokens("$.hooks.*[*].hooks[*].url").Select(t => t.Value<string>()).ToList();
            Assert.IsTrue(urls.All(u => u.Contains(":50000/")), "换端口重装后不留旧端口的钩子");
        }

        [Test]
        public void UninstallRestoresTheOriginal()
        {
            var installed = ClaudeHookInstaller.PreviewInstall(Existing, Config);
            Assert.IsTrue(ClaudeHookInstaller.IsInstalled(installed));
            var removed = ClaudeHookInstaller.PreviewUninstall(installed);
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(Existing), JObject.Parse(removed)));
            Assert.IsFalse(ClaudeHookInstaller.IsInstalled(removed));

            var fromEmpty = ClaudeHookInstaller.PreviewUninstall(ClaudeHookInstaller.PreviewInstall("{}", Config));
            Assert.IsTrue(JToken.DeepEquals(new JObject(), JObject.Parse(fromEmpty)), "原来没有 hooks 的，卸完也不留空壳");
        }

        [Test]
        public void RefusesToTouchBrokenSettings()
        {
            Assert.Throws<InvalidDataException>(() => ClaudeHookInstaller.PreviewInstall("{ \"a\": ", Config));
            Assert.Throws<InvalidDataException>(() => ClaudeHookInstaller.PreviewInstall("[1]", Config));
        }

        [Test]
        public void InstallWritesBackupAndUninstallRoundTrips()
        {
            var dir = Path.Combine(Path.GetTempPath(), "pet-claude-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "settings.json");
                File.WriteAllText(path, Existing);
                var backup = ClaudeHookInstaller.Install(Config, path);
                Assert.IsTrue(File.Exists(backup));
                Assert.AreEqual(Existing, File.ReadAllText(backup));
                Assert.IsTrue(ClaudeHookInstaller.IsInstalled(File.ReadAllText(path)));
                ClaudeHookInstaller.Uninstall(path);
                Assert.IsTrue(JToken.DeepEquals(JObject.Parse(Existing), JObject.Parse(File.ReadAllText(path))));
                Assert.IsNull(ClaudeHookInstaller.Uninstall(path), "没装时卸载什么都不做");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }

    public class BridgeHubTests
    {
        string _dir;
        BridgeHub _hub;
        HttpClient _http;

        static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pet-hub-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            var configPath = Path.Combine(_dir, "bridge.json");
            new BridgeConfig { Port = FreePort(), Token = "secret" }.Save(configPath);
            _hub = new BridgeHub(configPath, Path.Combine(_dir, "captures"), codexRoot: Path.Combine(_dir, "codex"));
            try
            {
                _hub.Start(watchCodex: false);
            }
            catch (InvalidOperationException error) when (error.InnerException is HttpListenerException)
            {
                Assert.Ignore("这台机器上 HttpListener 绑不了 127.0.0.1（需要 URL ACL）：" + error.InnerException.Message);
            }
            _http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:" + _hub.Port) };
        }

        [TearDown]
        public void TearDown()
        {
            _http?.Dispose();
            _hub?.Dispose();
            try { Directory.Delete(_dir, true); } catch (IOException) { }
        }

        HttpStatusCode Post(string path, string body, string token = "secret", string origin = null)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (token != null) req.Headers.Add("X-Pet-Token", token);
            if (origin != null) req.Headers.Add("Origin", origin);
            return _http.SendAsync(req).Result.StatusCode;
        }

        PetEvent Next()
        {
            for (var i = 0; i < 100; i++)
            {
                if (_hub.Events.TryDequeue(out var e)) return e;
                Thread.Sleep(10);
            }
            return null;
        }

        [Test]
        public void HealthNeedsNoToken()
        {
            Assert.AreEqual(HttpStatusCode.OK, _http.GetAsync("/health").Result.StatusCode);
        }

        [Test]
        public void RejectsBadTokenAndBrowserOrigins()
        {
            var body = JsonConvert.SerializeObject(new PetEvent { Id = "dsh:s:1", Source = PetSource.Dsh, Kind = PetEventKind.UserInput, Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
            Assert.AreEqual(HttpStatusCode.Unauthorized, Post("/event", body, token: "wrong"));
            Assert.AreEqual(HttpStatusCode.Unauthorized, Post("/event", body, token: null));
            Assert.AreEqual(HttpStatusCode.Forbidden, Post("/event", body, origin: "https://evil.example"));
            Assert.AreEqual(HttpStatusCode.NotFound, Post("/nope", body));
            Assert.IsFalse(_hub.Events.TryDequeue(out _), "被拒的请求不该进队列");
        }

        [Test]
        public void NormalizedEventsAndBatchesGoThroughTheGate()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var one = new PetEvent { Id = "dsh:s:1", Source = PetSource.Dsh, Kind = PetEventKind.UserInput, Ts = now, Tokens = 3 };
            Assert.AreEqual(HttpStatusCode.NoContent, Post("/event", JsonConvert.SerializeObject(one)));
            var got = Next();
            Assert.IsNotNull(got);
            Assert.AreEqual(3, got.Tokens);

            // 补发的一批里混着重复和过期的。
            var batch = new[]
            {
                one,
                new PetEvent { Id = "dsh:s:2", Source = PetSource.Dsh, Kind = PetEventKind.Generation, Ts = now, Tokens = 9 },
                new PetEvent { Id = "dsh:s:3", Source = PetSource.Dsh, Kind = PetEventKind.Generation, Ts = now - 60000, Tokens = 9 },
            };
            Assert.AreEqual(HttpStatusCode.NoContent, Post("/event", JsonConvert.SerializeObject(batch)));
            Assert.AreEqual("dsh:s:2", Next()?.Id);
            Assert.IsNull(Next(), "重复的和过期的都被闸门挡掉");
        }

        [Test]
        public void ClaudeHookPayloadsAreNormalized()
        {
            var payload = new JObject
            {
                ["hook_event_name"] = "PreToolUse",
                ["session_id"] = "cc1",
                ["tool_name"] = "Read",
                ["tool_use_id"] = "toolu_1",
                ["tool_input"] = new JObject { ["file_path"] = "/a/b/readme.md" },
            };
            Assert.AreEqual(HttpStatusCode.NoContent, Post("/hook/claude-code", payload.ToString()));
            var e = Next();
            Assert.AreEqual(PetEventKind.ToolCall, e.Kind);
            Assert.AreEqual("read", e.Tool);
            Assert.AreEqual("readme.md", e.File);
        }

        [Test]
        public void CapturesRawPayloadsWhenAsked()
        {
            _hub.CaptureRaw = true;
            Post("/hook/claude-code", "{\"hook_event_name\":\"Stop\",\"session_id\":\"cc1\"}");
            Next();
            var files = Directory.GetFiles(Path.Combine(_dir, "captures"));
            Assert.AreEqual(1, files.Length);
            StringAssert.Contains("Stop", File.ReadAllText(files[0]));
        }
    }
}
