using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DeepseekPet.Bridge
{
    /// <summary>
    /// 桥的总装：HTTP 监听（Claude Code 钩子 / dsh 转发 / 测试）+ Codex 轮询 → 同一个闸门 → 一个线程安全队列。
    ///
    /// 不碰任何 Unity API：后台线程只往 <see cref="Events"/> 里放，主线程每帧取（见 App 里的 BridgeHost）。
    /// 这样整个桥能在 dotnet test 里直接跑。
    /// </summary>
    public sealed class BridgeHub : IDisposable
    {
        /// <summary>过了闸门的事件。主线程取。</summary>
        public readonly ConcurrentQueue<PetEvent> Events = new ConcurrentQueue<PetEvent>();

        /// <summary>日志行（含被闸门挡掉的事件），调试面板用。</summary>
        public readonly ConcurrentQueue<string> Logs = new ConcurrentQueue<string>();

        public BridgeConfig Config { get; private set; }
        public int Port => _server?.Port ?? 0;
        public string CodexRoot => _codex?.Root;
        public int CodexFiles => _codex?.TrackedFiles ?? 0;

        /// <summary>打开后把每条原始负载存到 captureDir（适配器测试夹具就从这来）。</summary>
        public volatile bool CaptureRaw;

        readonly string _configPath;
        readonly string _captureDir;
        readonly Func<long> _nowMs;
        readonly EventGate _gate;
        readonly ClaudeCodeAdapter _claude = new ClaudeCodeAdapter();
        CodexWatcher _codex;
        BridgeServer _server;
        Timer _codexTimer;
        int _codexBusy;
        int _captureSeq;

        public BridgeHub(string configPath = null, string captureDir = null, Func<long> nowMs = null, string codexRoot = null)
        {
            _configPath = configPath ?? BridgeConfig.DefaultPath();
            _captureDir = captureDir ?? Path.Combine(BridgeConfig.HomeDir(), "captures");
            _nowMs = nowMs ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            _gate = new EventGate();
            _codex = codexRoot == null ? new CodexWatcher() : new CodexWatcher(codexRoot);
        }

        /// <summary>
        /// 起监听 + Codex 轮询。配置里的端口被占了就往后找一个空的，并把新端口写回配置
        /// （dsh 转发插件和 Claude 钩子都读这份配置）。
        /// </summary>
        public void Start(bool watchCodex = true)
        {
            Config = BridgeConfig.LoadOrCreate(_configPath);
            Exception last = null;
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var port = Config.Port + attempt;
                var server = new BridgeServer(port, Config.Token, OnBody, Log);
                try
                {
                    server.Start();
                }
                catch (HttpListenerException error)
                {
                    last = error;
                    server.Dispose();
                    continue;
                }
                _server = server;
                if (port != Config.Port)
                {
                    Log("bridge: 端口 " + Config.Port + " 被占用，改用 " + port);
                    Config.Port = port;
                    Config.Save(_configPath);
                }
                break;
            }
            if (_server == null) throw new InvalidOperationException("bridge: 找不到可用端口", last);
            Log("bridge: 监听 127.0.0.1:" + _server.Port);

            if (watchCodex)
            {
                _codexTimer = new Timer(_ => PollCodex(), null, 0, 500);
                Log("bridge: 追 Codex 会话 " + _codex.Root);
            }
        }

        /// <summary>手动轮询一次 Codex（测试用；正常由定时器每 500ms 调）。</summary>
        public void PollCodex()
        {
            if (Interlocked.Exchange(ref _codexBusy, 1) == 1) return;
            try
            {
                foreach (var e in _codex.Poll(DateTime.Now)) Offer(e);
            }
            catch (Exception error)
            {
                Log("codex: 轮询出错 " + error.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _codexBusy, 0);
            }
        }

        void OnBody(string route, string body)
        {
            Capture(route, body);
            try
            {
                if (route == "/hook/claude-code")
                {
                    var payload = JObject.Parse(body);
                    foreach (var e in _claude.Handle(payload, _nowMs())) Offer(e);
                }
                else if (route == "/event")
                {
                    // dsh 转发插件一次可能发一条，也可能攒一批补发。
                    var token = JToken.Parse(body);
                    if (token is JArray batch)
                    {
                        foreach (var item in batch) Offer(item.ToObject<PetEvent>());
                    }
                    else
                    {
                        Offer(token.ToObject<PetEvent>());
                    }
                }
            }
            catch (JsonException error)
            {
                Log("bridge: " + route + " 收到坏 JSON：" + error.Message);
            }
        }

        /// <summary>过闸门；放行的进队列，挡掉的只记日志。</summary>
        public void Offer(PetEvent e)
        {
            if (e == null) return;
            if (_gate.Admit(e, _nowMs()))
            {
                Events.Enqueue(e);
            }
            else
            {
                Log("gate: 挡掉 " + e.Id);
            }
        }

        void Capture(string route, string body)
        {
            if (!CaptureRaw) return;
            try
            {
                Directory.CreateDirectory(_captureDir);
                var name = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Interlocked.Increment(ref _captureSeq)
                    + (route == "/event" ? "-event" : "-claude") + ".json";
                File.WriteAllText(Path.Combine(_captureDir, name), body);
            }
            catch (IOException error)
            {
                Log("capture: 写不进去 " + error.Message);
            }
        }

        void Log(string line)
        {
            Logs.Enqueue(DateTime.Now.ToString("HH:mm:ss ") + line);
            while (Logs.Count > 500) Logs.TryDequeue(out _);
        }

        public void Dispose()
        {
            _codexTimer?.Dispose();
            _codexTimer = null;
            _server?.Dispose();
            _server = null;
        }
    }
}
