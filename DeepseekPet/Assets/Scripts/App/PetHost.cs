using System;
using System.Globalization;
using System.IO;
using System.Linq;
using DeepseekPet.Bridge;
using DeepseekPet.Core;
using DeepseekPet.Presentation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DeepseekPet.App
{
    /// <summary>
    /// 养成逻辑在 Unity 里的宿主：建 PetCore、把桥来的事件喂给它、驱动它的定时器和 10 秒一次的结算、退出时落盘。
    ///
    ///   数据表   Resources/PetData/pet-data.json（PetTools export-data 生成）
    ///   调参     ~/.deepseek-pet/pet-config.json（可选；格式同网页版 localStorage 的 dsh-pet-plugin/config）
    ///   存档     ~/.deepseek-pet/pet-save.json（格式同网页版，可以把网页存档粘进来导入）
    ///
    /// 画面交给 PetPresenter（透明置顶的小窗口 + 角色 + 气泡 / 特效）；这里另有一个调试面板（F2，编辑器里默认打开，
    /// 打包出来的程序里从右键菜单「调试面板」打开）。
    /// </summary>
    public sealed class PetHost : MonoBehaviour
    {
        const float TickSeconds = 10f;

        public static PetHost Instance { get; private set; }

        /// <summary>用哪个角色实现（CharacterViews 里登记的名字）。以后有了 Live2D / 3D 角色，在这里换。</summary>
        const string Character = "sprite";

        PetCore _core;
        PetPresenter _presenter;
        FileStorage _storage;
        string _error;
        float _nextTick;

        bool _showPanel;
        Rect _window = new Rect(680, 20, 420, 560);
        string _status = "";
        string _lastBubbleKey;
        readonly System.Collections.Generic.List<string> _feed = new System.Collections.Generic.List<string>();
        readonly System.Collections.Generic.HashSet<string> _seenEffects = new System.Collections.Generic.HashSet<string>();

        /// <summary>当前的宠物（没起来时为 null）。表现层读它的 State、订阅 Changed。</summary>
        public PetCore Core => _core;

        public static string ConfigPath()
        {
            return Path.Combine(Path.GetDirectoryName(FileStorage.DefaultPath()), "pet-config.json");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            // 带着一次性命令启动（--connect-claude 之类）时不起来，见 CommandLine。
            if (Instance != null || CommandLine.Handled) return;
            var go = new GameObject("PetHost");
            DontDestroyOnLoad(go);
            go.AddComponent<PetHost>();
        }

        void Awake()
        {
            Instance = this;
            _showPanel = Application.isEditor;
        }

        void Start()
        {
            // Start 在所有 Awake 之后：这时 BridgeHost 已经知道自己是不是第二份。
            if (BridgeHost.Instance != null && BridgeHost.Instance.IsSecondInstance)
            {
                _error = "已经有一个桌宠在运行了（两份会抢同一个存档）";
                // 打包出来的程序直接退出（编辑器里留着，好看到这句话）。
                if (!Application.isEditor) Application.Quit();
                return;
            }
            try
            {
                _storage = new FileStorage(FileStorage.DefaultPath());
                _core = CreateCore();
            }
            catch (Exception error)
            {
                _error = error.Message;
                Debug.LogException(error);
                return;
            }
            BridgeHost.EventReceived += OnEvent;
            _presenter = PetPresenter.Create(Character);
            _presenter.Bind(_core);
            _presenter.AddMenuItem("🔌",
                () => BridgeHost.Instance != null && BridgeHost.Instance.ClaudeConnected ? "断开 Claude Code" : "连接 Claude Code",
                () => BridgeHost.Instance?.ToggleClaude(),
                () => BridgeHost.Instance != null && BridgeHost.Instance.CanConnectClaude);
            _presenter.AddMenuItem("🛠", () => _presenter.DebugMode ? "关掉调试面板" : "调试面板", () => _presenter.DebugMode = !_presenter.DebugMode);
            _presenter.AddMenuItem("👋", () => "退出", Application.Quit);
            _presenter.DebugModeChanged += on =>
            {
                _showPanel = on;
                BridgeHost.ShowPanel = on;
            };
            if (!Application.isEditor) BridgeHost.ShowPanel = false;
            _nextTick = Time.unscaledTime + TickSeconds;
        }

        PetCore CreateCore()
        {
            var asset = Resources.Load<TextAsset>("PetData/pet-data");
            if (asset == null) throw new FileNotFoundException("找不到 Resources/PetData/pet-data.json（跑一下 PetTools export-data）");
            var data = PetData.Parse(asset.text);
            var core = new PetCore(data, PetConfig.Create(data, ReadOverrides()), new SystemClock(), Mulberry32.FromTime(), _storage);
            core.Changed += OnChanged;
            return core;
        }

        /// <summary>调参文件：不存在 / 读坏了都用默认值（读坏了在面板上说一声）。</summary>
        JObject ReadOverrides()
        {
            var path = ConfigPath();
            if (!File.Exists(path)) return null;
            try
            {
                return JObject.Parse(File.ReadAllText(path));
            }
            catch (Exception error) when (error is JsonException || error is IOException)
            {
                _status = "pet-config.json 读不了，已用默认值：" + error.Message;
                return null;
            }
        }

        void OnEvent(PetEvent e)
        {
            _core?.HandleEvent(e.Kind, e.Tokens, e.Output, e.Tool, e.CallId, e.File, e.Failed);
        }

        void Update()
        {
            if (_core == null) return;
            _core.Update();
            if (Time.unscaledTime >= _nextTick)
            {
                _nextTick = Time.unscaledTime + TickSeconds;
                _core.Tick();
            }
            if (Input.GetKeyDown(KeyCode.F2)) _showPanel = !_showPanel;
        }

        /// <summary>把新出现的气泡 / 特效记进面板的流水（P2 换成真正的表现）。</summary>
        void OnChanged()
        {
            var s = _core.State;
            if (s.Bubble != null && s.Bubble.Key != _lastBubbleKey)
            {
                _lastBubbleKey = s.Bubble.Key;
                Log("💬 " + s.Bubble.Text);
            }
            foreach (var e in s.Effects)
            {
                if (!_seenEffects.Add(e.Key)) continue;
                Log(e.Text != null ? e.Icon + " " + e.Text : e.Icon + " +" + e.FoodAmount + " 饱食 +" + e.ExpAmount + " 经验" + (e.ComboCount > 1 ? " ×" + e.ComboMultiplier.ToString("0.0", CultureInfo.InvariantCulture) : ""));
            }
            if (_seenEffects.Count > 500) _seenEffects.Clear();
        }

        void Log(string line)
        {
            _feed.Add(DateTime.Now.ToString("HH:mm:ss ") + line);
            if (_feed.Count > 12) _feed.RemoveAt(0);
        }

        /// <summary>
        /// 导入一份存档（网页版 localStorage["dsh-pet-plugin/state"] 的内容，v1 / v2 都行）。
        /// 当前存档先另存一份 .before-import，再用导入的内容重建宠物。
        /// </summary>
        public string ImportSave(string json)
        {
            if (_core == null) return "宠物没起来";
            JObject parsed;
            try
            {
                parsed = JObject.Parse(json);
            }
            catch (JsonException)
            {
                return "剪贴板里不是 JSON";
            }
            var v = parsed.Value<int?>("v");
            if (v != 1 && v != 2) return "不是宠物存档（没有 v: 1 / 2）";
            _core.Dispose();
            try
            {
                if (File.Exists(_storage.Path)) File.Copy(_storage.Path, _storage.Path + ".before-import", true);
                _storage.Write(json);
            }
            catch (IOException error)
            {
                _core = CreateCore();
                _presenter?.Bind(_core);
                return "没导进去：" + error.Message;
            }
            _core = CreateCore();
            _presenter?.Bind(_core);
            return "已导入（原存档在 pet-save.json.before-import）";
        }

        void OnApplicationQuit()
        {
            _core?.Dispose();
        }

        void OnDestroy()
        {
            BridgeHost.EventReceived -= OnEvent;
            _core?.Dispose();
            _core = null;
            if (Instance == this) Instance = null;
        }

        void OnGUI()
        {
            if (!_showPanel) return;
            _window = GUILayout.Window(4783, _window, DrawPanel, "宠物（F2 隐藏）");
        }

        static string Bar(int value)
        {
            var filled = Mathf.Clamp(value / 10, 0, 10);
            return new string('█', filled) + new string('░', 10 - filled) + " " + value;
        }

        void DrawPanel(int id)
        {
            if (_core == null)
            {
                GUILayout.Label("宠物没起来：" + _error);
                GUI.DragWindow();
                return;
            }
            var s = _core.State;
            var p = s.Pet;
            var cfg = _core.Config;
            GUILayout.Label(p.Name + " · " + _core.LookLabel + " · Lv." + p.Level + "（" + p.Exp + "/" + p.Level * _core.Data.ExpPerLevel + "）"
                + (s.Prestige > 0 ? " · 第 " + (s.Prestige + 1) + " 世" : "") + (s.Asleep ? " · 💤" : "") + (s.Buff != null ? " · 🔥暴食" : ""));
            GUILayout.Label("饥饿 " + Bar(p.Hunger) + (p.Hunger >= cfg.HungryAt ? "  饿了！" : ""));
            GUILayout.Label("心情 " + Bar(p.Mood) + " " + PetRules.MoodFaceOf(p.Mood));
            GUILayout.Label("精力 " + Bar(p.Energy));
            var dim = PetRules.MoodDimOf(_core.Data, p, cfg);
            GUILayout.Label("好奇 " + p.Curiosity + "  得意 " + p.Pride + "  担忧 " + p.Concern + (dim != null ? "  脸上：" + dim.Icon + dim.Label : ""));
            GUILayout.Label("技能 " + string.Join("  ", _core.Data.Skills.Select(k => k.Icon + k.Label + " " + s.Skills[k.Key].Level)));
            GUILayout.Label("连击 " + s.ComboCount + "  零食 " + s.Snacks + "/" + cfg.ManualSnackMax + "  累计 " + s.TotalFeeds + " 口 / "
                + PetRules.FormatTokens(s.TotalTokens) + " tok  成就 " + s.Achievements.Count + "/" + _core.Data.Achievements.Count);
            var quests = PetRules.PickDailyQuests(_core.Data, s.Daily.Day);
            GUILayout.Label("今日 " + string.Join("  ", quests.Select(q => (s.Daily.Done.Contains(q.Id) ? "✔" : "·") + q.Label)));
            GUILayout.Label("宠物 " + (s.PetOrder.IndexOf(s.ActivePetId) + 1) + "/" + s.PetCount + "  蛋 " + s.Eggs.Count);
            GUILayout.Label(s.Bubble != null ? "💬 " + s.Bubble.Text : " ");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("摸头")) _core.Pat();
            if (GUILayout.Button("零食")) _core.Snack();
            if (GUILayout.Button("上一只")) _core.SwitchPet(-1);
            if (GUILayout.Button("下一只")) _core.SwitchPet(1);
            if (s.Eggs.Count > 0 && GUILayout.Button("孵蛋"))
            {
                _core.StartHatch(s.Eggs[0].Id);
                _core.ConfirmHatch(null);
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("从剪贴板导入网页存档")) _status = ImportSave(GUIUtility.systemCopyBuffer ?? "");
            if (GUILayout.Button("立刻存档"))
            {
                _core.Flush();
                _status = "已存到 " + _storage.Path;
            }
            GUILayout.EndHorizontal();
            if (_status != "") GUILayout.Label(_status);
            foreach (var line in _feed) GUILayout.Label(line);
            GUI.DragWindow();
        }
    }
}
