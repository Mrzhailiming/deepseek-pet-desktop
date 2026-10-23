using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using DeepseekPet.Bridge;
using UnityEngine;

namespace DeepseekPet.App
{
    /// <summary>
    /// 桥在 Unity 里的宿主：进 Play / 启动程序时自己建出来（不依赖场景），常驻不销毁。
    ///
    /// 后台线程（HTTP / Codex 轮询）只往 BridgeHub 的队列里放；这里每帧在主线程取出来，
    /// 再通过 <see cref="EventReceived"/> 交给 PetHost 喂给宠物，同时显示在调试面板上（F1）。
    /// </summary>
    public sealed class BridgeHost : MonoBehaviour
    {
        /// <summary>每条过了闸门的事件，在主线程上触发。</summary>
        public static event Action<PetEvent> EventReceived;

        public static BridgeHost Instance { get; private set; }

        /// <summary>每帧最多处理多少条，免得一大批补发把一帧卡住。</summary>
        const int MaxPerFrame = 200;

        BridgeHub _hub;
        Mutex _single;
        string _startError;

        readonly List<string> _lines = new List<string>();
        readonly Dictionary<string, int> _counts = new Dictionary<string, int>();
        readonly Dictionary<string, long> _tokens = new Dictionary<string, long>();
        string _status = "";
        bool _claudeInstalled;
        Vector2 _scroll;
        Rect _window = new Rect(20, 20, 640, 520);

        public BridgeHub Hub => _hub;

        /// <summary>调试面板开不开（F1 切换；打包出来的程序里由右键菜单「调试面板」一起打开）。</summary>
        public static bool ShowPanel { get; set; } = true;

        /// <summary>本机已经有一份桌宠在跑：这一份不起桥，PetHost 也不起宠物（免得两份抢同一个存档）。</summary>
        public bool IsSecondInstance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            // 带着一次性命令启动（--connect-claude 之类）时不起来，见 CommandLine。
            if (Instance != null || CommandLine.Handled) return;
            var go = new GameObject("PetBridge");
            DontDestroyOnLoad(go);
            go.AddComponent<BridgeHost>();
        }

        void Awake()
        {
            Instance = this;
            // 桌宠大部分时间不在前台，失焦也得照常收事件。
            Application.runInBackground = true;

            // 同一台机器只跑一份：第二份起不来监听，事件也会被两边抢。
            _single = new Mutex(true, "Local\\DeepseekPetBridge", out var created);
            if (!created)
            {
                _single.Dispose();
                _single = null;
                _startError = "已经有一个桌宠在运行了";
                IsSecondInstance = true;
                return;
            }

            _hub = new BridgeHub();
            try
            {
                _hub.Start();
            }
            catch (Exception error)
            {
                _startError = error.Message;
                _hub.Dispose();
                _hub = null;
                return;
            }
            RefreshClaudeStatus();
        }

        void Update()
        {
            if (_hub == null) return;
            for (var i = 0; i < MaxPerFrame && _hub.Events.TryDequeue(out var e); i++)
            {
                Count(e);
                Push(DateTime.Now.ToString("HH:mm:ss ") + e);
                EventReceived?.Invoke(e);
            }
            while (_hub.Logs.TryDequeue(out var log)) Push("· " + log);
            if (Input.GetKeyDown(KeyCode.F1)) ShowPanel = !ShowPanel;
        }

        void Count(PetEvent e)
        {
            var key = e.Source + "/" + e.Kind;
            _counts.TryGetValue(key, out var n);
            _counts[key] = n + 1;
            _tokens.TryGetValue(e.Source, out var t);
            _tokens[e.Source] = t + (PetEventKind.Feeds(e.Kind) ? e.Tokens : 0);
        }

        void Push(string line)
        {
            _lines.Add(line);
            if (_lines.Count > 300) _lines.RemoveRange(0, _lines.Count - 300);
            _scroll.y = float.MaxValue;
        }

        void RefreshClaudeStatus()
        {
            try
            {
                var path = ClaudeHookInstaller.DefaultSettingsPath();
                _claudeInstalled = File.Exists(path) && ClaudeHookInstaller.IsInstalled(File.ReadAllText(path));
            }
            catch (IOException)
            {
                _claudeInstalled = false;
            }
        }

        void OnGUI()
        {
            if (!ShowPanel) return;
            _window = GUILayout.Window(4782, _window, DrawPanel, "桌宠事件桥（F1 隐藏）");
        }

        void DrawPanel(int id)
        {
            if (_startError != null)
            {
                GUILayout.Label("桥没起来：" + _startError);
                GUI.DragWindow();
                return;
            }

            GUILayout.Label("监听 127.0.0.1:" + _hub.Port + "    配置 " + BridgeConfig.DefaultPath());
            GUILayout.Label("Codex：" + _hub.CodexRoot + "（正在追 " + _hub.CodexFiles + " 个会话文件）");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Claude Code：" + (_claudeInstalled ? "已连接" : "未连接"), GUILayout.Width(150));
            if (GUILayout.Button(_claudeInstalled ? "断开 Claude Code" : "连接 Claude Code", GUILayout.Width(150)))
            {
                ToggleClaudeCore();
            }
            _hub.CaptureRaw = GUILayout.Toggle(_hub.CaptureRaw, "保存原始负载");
            if (GUILayout.Button("清空", GUILayout.Width(60)))
            {
                _lines.Clear();
                _counts.Clear();
                _tokens.Clear();
            }
            GUILayout.EndHorizontal();
            if (_status != "") GUILayout.Label(_status);

            foreach (var source in new[] { PetSource.ClaudeCode, PetSource.Codex, PetSource.Dsh })
            {
                _tokens.TryGetValue(source, out var tokens);
                var parts = new List<string>();
                foreach (var kind in new[] { PetEventKind.UserInput, PetEventKind.Generation, PetEventKind.ToolCall, PetEventKind.ToolResult, PetEventKind.TurnEnd })
                {
                    _counts.TryGetValue(source + "/" + kind, out var n);
                    parts.Add(kind + " " + n);
                }
                GUILayout.Label(source + "：" + string.Join("  ", parts) + "  喂食 token " + tokens);
            }

            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (var line in _lines) GUILayout.Label(line);
            GUILayout.EndScrollView();
            GUI.DragWindow();
        }

        /// <summary>Claude Code 的钩子装上了没有（右键 / 托盘菜单显示用）。</summary>
        public bool ClaudeConnected
        {
            get
            {
                // 「连接AI助手」脚本可能在桌宠开着的时候改了 settings.json：最多每秒重读一次。
                if (Time.unscaledTime >= _claudeCheckedAt)
                {
                    _claudeCheckedAt = Time.unscaledTime + 1;
                    RefreshClaudeStatus();
                }
                return _claudeInstalled;
            }
        }

        float _claudeCheckedAt;

        /// <summary>桥起来了才能装钩子（要往 settings.json 里写端口和口令）。</summary>
        public bool CanConnectClaude => _hub != null;

        /// <summary>最近一次连接 / 断开的结果（成功时带备份文件的路径）。</summary>
        public string ClaudeStatus => _status;

        /// <summary>装 / 卸 Claude Code 的钩子（先备份 settings.json）。菜单和 F1 面板的按钮都走这里。</summary>
        public void ToggleClaude()
        {
            if (_hub == null) return;
            ToggleClaudeCore();
        }

        void ToggleClaudeCore()
        {
            try
            {
                if (_claudeInstalled)
                {
                    var backup = ClaudeHookInstaller.Uninstall();
                    _status = "已断开。原设置备份在 " + backup;
                }
                else
                {
                    var backup = ClaudeHookInstaller.Install(_hub.Config);
                    _status = "已连接，新开的 Claude Code 会话生效。" + (backup != null ? "原设置备份在 " + backup : "");
                }
            }
            catch (Exception error)
            {
                _status = "没改成：" + error.Message;
            }
            RefreshClaudeStatus();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            _hub?.Dispose();
            _hub = null;
            if (_single != null)
            {
                _single.ReleaseMutex();
                _single.Dispose();
                _single = null;
            }
        }
    }
}
