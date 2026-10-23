using System;
using System.Collections;
using System.Collections.Generic;
using DeepseekPet.Core;
using DeepseekPet.Platform;
using UnityEngine;

namespace DeepseekPet.Presentation
{
    /// <summary>
    /// 桌宠的表现层：把 PetCore 的状态画出来、把鼠标变成摸头 / 拖动 / 右键菜单，并管着那个透明置顶的小窗口。
    ///
    ///   - 角色交给 <see cref="ICharacterView"/>（世界空间，正交相机）；气泡、状态条、特效是覆盖层 UI；
    ///   - 窗口只有宠物那么大（约 300×380，按 DPI 缩放），拖宠物就是拖窗口；鼠标不在宠物 / 菜单上时整窗点击穿透；
    ///   - 没事的时候降到 12 帧，有动静回到 30 帧。
    /// 状态变化按「和上一份快照比」来认：新特效、eatKey / patKey 变了、升级了、小动作变了、气泡换了。
    /// </summary>
    public sealed class PetPresenter : MonoBehaviour
    {
        /// <summary>窗口的逻辑尺寸（96 dpi 下的像素）。</summary>
        public const int WindowWidth = 300;
        public const int WindowHeight = 380;
        /// <summary>调试模式（显示 IMGUI 面板）时的窗口大小。</summary>
        public const int DebugWidth = 1120;
        public const int DebugHeight = 660;

        /// <summary>角色在宠物框里占的区域（逻辑像素，相对宠物框左下角）：脚下留出状态条，头顶留出气泡。</summary>
        static readonly Rect CharacterArea = new Rect(40, 58, 220, 200);

        Camera _camera;
        Transform _world;
        ICharacterView _view;
        SpriteRenderer _glow;
        Canvas _canvas;
        UnityEngine.UI.CanvasScaler _scaler;
        RectTransform _ui;
        RectTransform _fxRoot;
        readonly FxLayer _fx = new FxLayer();
        PetHud _hud;
        PetMenu _menu;
        PetPanel _panel;
        PetMiniGame _miniGame;
        readonly List<PetMenu.Item> _hostItems = new List<PetMenu.Item>();

        PetCore _core;
        PetState _last;
        readonly HashSet<string> _seenEffects = new HashSet<string>();
        float _scale = 1;
        bool _windowReady;
        bool _debugMode;

        // 鼠标
        bool _prevLeft;
        bool _prevRight;
        bool _pressOnPet;
        bool _dragging;
        Vector2Int _pressCursor;
        ScreenRect _pressWindow;
        float _hoverUntil;

        float _zzzAt;
        float _bannerAt;
        int _bannerStack;
        float _lastActivity;
        float _topmostAt;
        float _traceAt;

        /// <summary>
        /// 诊断开关（环境变量 DEEPSEEK_PET_TRACE）：
        ///   1     每秒往 Player.log 打一行鼠标 / 窗口 / 命中的状态（排查点击穿透用）；
        ///   menu  同上，另外启动 3 秒后自己打开右键菜单（没法真点右键时检查菜单长相）；
        ///   panel 同上，另外自己打开宠物面板、逐页翻一遍，再开一局小游戏，每一步存一张 ~/.deepseek-pet/trace-*.png。
        /// </summary>
        static readonly string TraceMode = Environment.GetEnvironmentVariable("DEEPSEEK_PET_TRACE");
        static readonly bool Trace = TraceMode == "1" || TraceMode == "menu" || TraceMode == "panel";
        bool _demoMenuShown;

        public PetCore Core => _core;
        public ICharacterView View => _view;

        /// <summary>调试模式：窗口放大到能摆下 IMGUI 面板，并且不再点击穿透。</summary>
        public bool DebugMode
        {
            get => _debugMode;
            set
            {
                if (_debugMode == value) return;
                _debugMode = value;
                DesktopWindow.SetNoActivate(!value);
                ApplyWindowSize();
                DebugModeChanged?.Invoke(value);
            }
        }

        public event Action<bool> DebugModeChanged;

        /// <summary>宠物面板（状态 / 技能 / 成就 / 任务 / 蛋 / 宠物）：打开时窗口往左扩出一块放它，宠物留在原地。</summary>
        public bool PanelOpen
        {
            get => _panel.Open;
            set
            {
                if (_panel.Open == value) return;
                if (value) _panel.Show(PanelOrigin);
                else _panel.Hide();
                ApplyWindowSize();
            }
        }

        Vector2 PanelOrigin => new Vector2(PetBox.xMin - PetPanel.Width - 6, PetBox.yMin + 6);

        /// <summary>建一个常驻的表现层；角色实现按名字挑（见 <see cref="CharacterViews"/>）。</summary>
        public static PetPresenter Create(string character)
        {
            var go = new GameObject("PetPresenter");
            DontDestroyOnLoad(go);
            var presenter = go.AddComponent<PetPresenter>();
            presenter.Build(character);
            return presenter;
        }

        /// <summary>换一只宠物（导入存档后会换成新的 PetCore）。</summary>
        public void Bind(PetCore core)
        {
            _core = core;
            _last = core?.State;
            _seenEffects.Clear();
            _fx.Clear();
            _hud.HideBubble();
            if (_last != null)
            {
                foreach (var e in _last.Effects) _seenEffects.Add(e.Key);
                if (_last.Bubble != null) _hud.ShowBubble(_last.Bubble.Kind, _last.Bubble.Text);
                ApplyLook(_last);
            }
        }

        /// <summary>宿主往右键菜单里加一项（调试面板、退出之类）。</summary>
        public void AddMenuItem(string icon, Func<string> label, Action run, Func<bool> visible = null)
        {
            _hostItems.Add(new PetMenu.Item { Icon = icon, Label = label, Run = run, Visible = visible });
        }

        void Build(string character)
        {
            Application.targetFrameRate = 30;
            QualitySettings.vSyncCount = 0;

            var cameraGo = new GameObject("PetCamera");
            cameraGo.transform.SetParent(transform, false);
            _camera = cameraGo.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            // 透明窗口的关键：清成 alpha 0，DWM 才会把背景透出来。
            _camera.backgroundColor = new Color(0, 0, 0, 0);
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 100f;

            _world = new GameObject("World").transform;
            _world.SetParent(transform, false);
            var glowGo = new GameObject("FrenzyGlow");
            glowGo.transform.SetParent(_world, false);
            _glow = glowGo.AddComponent<SpriteRenderer>();
            _glow.sprite = UiKit.Glow;
            _glow.sortingOrder = -10;
            _glow.color = new Color(1, 0.45f, 0.15f, 0);
            _view = CharacterViews.Create(character, _world);

            var canvasGo = new GameObject("PetCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            _scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
            _ui = (RectTransform)canvasGo.transform;
            _hud = new PetHud(_ui);
            _fxRoot = UiKit.Node("Fx", _ui);
            _fxRoot.anchorMin = Vector2.zero;
            _fxRoot.anchorMax = Vector2.one;
            _fxRoot.offsetMin = _fxRoot.offsetMax = Vector2.zero;
            _miniGame = new PetMiniGame(_ui);
            _panel = new PetPanel(_ui, () => PanelOpen = false);
            _menu = new PetMenu(_ui);

            if (DesktopWindow.IsSupported) StartCoroutine(InitWindow());
        }

        IEnumerator InitWindow()
        {
            // 先按主屏的 DPI 定一个大小，挪到位置后再按所在显示器的 DPI 校正一次。
            var scale = DesktopWindow.Scale;
            var w = Mathf.RoundToInt(WindowWidth * scale);
            var h = Mathf.RoundToInt(WindowHeight * scale);
            Screen.SetResolution(w, h, FullScreenMode.Windowed);
            yield return null;
            yield return null;
            var pos = WindowPlacement.Load(w, h);
            if (!DesktopWindow.Init(pos.x, pos.y, w, h))
            {
                Debug.LogWarning("[pet] 找不到自己的窗口，透明 / 置顶没生效");
                yield break;
            }
            _windowReady = true;
            yield return null;
            ApplyWindowSize();
            TrayIcon.Add("桌宠", TrayItems());
        }

        /// <summary>按当前显示器的 DPI 和调试模式定窗口大小；放大 / 缩小时右下角不动（宠物待在原地）。</summary>
        void ApplyWindowSize()
        {
            if (!_windowReady) return;
            var scale = DesktopWindow.Scale;
            var r = DesktopWindow.WindowRect;
            var logicalW = _debugMode ? DebugWidth : _panel.Open ? WindowWidth + PetPanel.Width + 12 : WindowWidth;
            var logicalH = _debugMode ? DebugHeight : WindowHeight;
            var w = Mathf.RoundToInt(logicalW * scale);
            var h = Mathf.RoundToInt(logicalH * scale);
            if (w == r.Width && h == r.Height) return;
            // 右下角不动；但往左上扩的时候别扩到屏幕外面去（宠物靠在屏幕左边时打开面板）。
            var x = r.Right - w;
            var y = r.Bottom - h;
            var work = DesktopWindow.WorkAreaAt(r.Left + r.Width / 2, r.Top + r.Height / 2);
            x = Mathf.Max(x, work.Left);
            y = Mathf.Max(y, work.Top);
            DesktopWindow.SetBounds(x, y, w, h);
        }

        // ─── 坐标 ───

        Vector2 ToLogical(Vector2 screen) => screen / _scale;
        Vector2 ToScreen(Vector2 logical) => logical * _scale;

        /// <summary>整只宠物占的框（逻辑像素）：窗口右下角那 300×380。调试模式下窗口变大，宠物还在右下角。</summary>
        Rect PetBox
        {
            get
            {
                var w = Screen.width / _scale;
                return new Rect(w - WindowWidth, 0, WindowWidth, WindowHeight);
            }
        }

        Rect LogicalCharacterArea
        {
            get
            {
                var box = PetBox;
                return new Rect(box.x + CharacterArea.x, box.y + CharacterArea.y, CharacterArea.width, CharacterArea.height);
            }
        }

        Rect ScreenToLogical(Rect r) => new Rect(r.x / _scale, r.y / _scale, r.width / _scale, r.height / _scale);

        // ─── 每帧 ───

        void Update()
        {
            var dt = Time.unscaledDeltaTime;
            var scale = _windowReady ? DesktopWindow.Scale : 1f;
            // 拖到另一块 DPI 不同的屏上：窗口按新的缩放重算大小（右下角不动）。
            if (_windowReady && !Mathf.Approximately(scale, _scale)) ApplyWindowSize();
            _scale = scale;
            _scaler.scaleFactor = _scale;
            _camera.orthographicSize = Screen.height / 2f / PetStageCamera.PixelsPerUnit;
            _camera.transform.localPosition = new Vector3(Screen.width / 2f / PetStageCamera.PixelsPerUnit, Screen.height / 2f / PetStageCamera.PixelsPerUnit, -10);

            var area = LogicalCharacterArea;
            _view.Layout(new Rect(ToScreen(area.position), ToScreen(area.size)));

            if (_core != null)
            {
                var state = _core.State;
                if (state != _last) Apply(state);
                StepAmbient(state);
            }

            TrayIcon.Pump();
            HandleMouse();
            if (_panel.Open) _panel.Move(PanelOrigin);
            _panel.Step(_core, ToLogical(Cursor()));
            _miniGame.Step(_core, PetBox);
            _fx.Step(dt);
            StepHud(dt);
            StepGlow();
            StepWindow();
        }

        // ─── 状态 → 画面 ───

        static CharacterAction? ActionOfIdle(string idle)
        {
            switch (idle)
            {
                case "yawn": return CharacterAction.Yawn;
                case "wag": return CharacterAction.Wag;
                case "roll": return CharacterAction.Roll;
                case "peek": return CharacterAction.Peek;
                default: return null;
            }
        }

        void ApplyLook(PetState s)
        {
            var data = _core.Data;
            var cfg = _core.Config;
            var pet = s.Pet;
            var form = PetRules.FormOf(data, pet.Form);
            var stage = PetRules.WhaleStageOf(data, pet.Level);
            var record = _core.Collection[s.ActivePetId];
            _view.SetLook(new CharacterLook
            {
                Stage = stage.Key,
                Form = form?.Key ?? "",
                Species = record?.Species ?? "whale",
                Scale = (form != null ? form.Size : stage.Size) / 64f,
            });
            // 状态优先级和网页版一样：饿 > 睡 > 心情差 > 连击兴奋 > 普通。
            var excited = s.ComboTier == "epic";
            var asleep = s.Asleep && !excited;
            var starving = pet.Hunger >= cfg.HungryAt && !excited && !asleep;
            var sad = cfg.VitalsEnabled && pet.Mood < cfg.MoodSadAt && !excited && !asleep && !starving;
            _view.SetState(starving ? CharacterState.Hungry : asleep ? CharacterState.Sleep : sad ? CharacterState.Sad : excited ? CharacterState.Excited : CharacterState.Normal);
            _view.SetMoodDim(PetRules.MoodDimOf(data, pet, cfg)?.Key);
            _view.SetFrenzy(s.Buff != null);
        }

        void Apply(PetState s)
        {
            var prev = _last;
            _last = s;
            _lastActivity = Time.unscaledTime;
            if (prev != null && prev.ActivePetId != s.ActivePetId)
            {
                // 换了一只：旧的特效、气泡都不算数。
                _fx.Clear();
                _seenEffects.Clear();
                _hud.HideBubble();
                prev = null;
            }
            ApplyLook(s);

            var fed = false;
            var evolved = false;
            foreach (var e in s.Effects)
            {
                if (!_seenEffects.Add(e.Key)) continue;
                if (e.Text == null)
                {
                    SpawnFood(e);
                    fed = true;
                }
                else
                {
                    if (e.Source == "evolve" || e.Source == "morph") evolved = true;
                    SpawnNotice(e);
                }
            }
            if (_seenEffects.Count > 400) _seenEffects.Clear();
            if (prev == null) return;

            // 关了特效开关时没有食物飞进来，进食动作直接演。
            if (s.EatKey != prev.EatKey && !fed) _view.Play(CharacterAction.Eat);
            if (s.PatKey != prev.PatKey)
            {
                _view.Play(CharacterAction.Pat);
                SpawnHearts();
            }
            if (s.Pet.Level > prev.Pet.Level && !evolved) _view.Play(CharacterAction.LevelUp);
            if (s.IdleAct != null && s.IdleAct != prev.IdleAct)
            {
                var act = ActionOfIdle(s.IdleAct);
                if (act.HasValue) _view.Play(act.Value);
            }
            if (s.Bubble != prev.Bubble)
            {
                if (s.Bubble == null) _hud.HideBubble();
                else _hud.ShowBubble(s.Bubble.Kind, s.Bubble.Text);
            }
        }

        static int FoodSize(string tier)
        {
            switch (tier)
            {
                case "feast": return 50;
                case "large": return 38;
                case "small": return 30;
                default: return 24;
            }
        }

        static Color SourceColor(string source)
        {
            switch (source)
            {
                case FeedSource.UserInput: return new Color32(0x9b, 0xe8, 0x7a, 0xff);
                case FeedSource.Generation: return new Color32(0x8f, 0xc3, 0xff, 0xff);
                case FeedSource.ToolResult: return new Color32(0xff, 0xb8, 0x6b, 0xff);
                default: return new Color32(0xff, 0xa8, 0xd8, 0xff);
            }
        }

        /// <summary>一口食物从窗口边上飞进嘴里；到了再演进食、飘经验。</summary>
        void SpawnFood(PetEffect e)
        {
            var box = PetBox;
            Vector2 from;
            var side = UnityEngine.Random.value;
            if (side < 0.4f) from = new Vector2(box.xMin + 10, box.yMin + UnityEngine.Random.Range(110f, 300f));
            else if (side < 0.8f) from = new Vector2(box.xMax - 10, box.yMin + UnityEngine.Random.Range(110f, 300f));
            else from = new Vector2(box.xMin + UnityEngine.Random.Range(40f, box.width - 40), box.yMax - 12);
            Func<Vector2> to = () => ToLogical(_view.MouthScreenPoint);
            var exp = e.ExpAmount;
            var combo = e.ComboCount;
            var multiplier = e.ComboMultiplier;
            var tier = e.Tier;
            _fx.Add(new FoodFlight(_fxRoot, e.Icon, FoodSize(e.FoodTier), SourceColor(e.Source), from, to, () =>
            {
                _view.Play(CharacterAction.Eat);
                var mouth = to();
                var color = tier == "gold" ? UiKit.Colors.Gold : Color.white;
                var text = "+" + exp + (combo > 1 ? "  ×" + multiplier.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "");
                _fx.Add(new FloatingText(_fxRoot, "⭐", text, tier == "epic" ? 19 : 16, color, mouth + new Vector2(UnityEngine.Random.Range(-24f, 24f), 26), 1.1f, 40, tier == "epic"));
            }));
        }

        /// <summary>一条通知（进阶 / 成就 / 任务 / 技能 …）：从头顶飘起一行字；同一瞬间的几条错开叠放。</summary>
        void SpawnNotice(PetEffect e)
        {
            if (e.Source == "pat") return; // 摸头有爱心，不再飘「摸摸头」三个字。
            if (e.Source == "evolve") _view.Play(CharacterAction.Evolve);
            if (e.Source == "morph") _view.Play(CharacterAction.Morph);
            var head = ScreenToLogical(_view.ScreenBounds);
            if (Time.unscaledTime - _bannerAt > 0.6f) _bannerStack = 0;
            _bannerAt = Time.unscaledTime;
            var y = Mathf.Min(head.yMax - 6, PetBox.yMax - 60) - _bannerStack * 24;
            _bannerStack++;
            var x = Mathf.Clamp(head.center.x, PetBox.xMin + 110, PetBox.xMax - 110);
            var epic = e.Tier == "epic";
            var color = epic ? Color.white : e.Tier == "gold" ? UiKit.Colors.Gold : Color.white;
            _fx.Add(new FloatingText(_fxRoot, e.Icon, e.Text, epic ? 18 : 15, color, new Vector2(x, y), 2.4f, 34, epic));
        }

        void SpawnHearts()
        {
            var head = ScreenToLogical(_view.ScreenBounds);
            for (var i = 0; i < 3; i++)
            {
                var from = new Vector2(head.center.x + UnityEngine.Random.Range(-30f, 30f), head.yMax - 20 + UnityEngine.Random.Range(-10f, 10f));
                _fx.Add(new Drift(_fxRoot, "♥", UnityEngine.Random.Range(16, 24), UiKit.Colors.Mood, from, new Vector2(UnityEngine.Random.Range(-12f, 12f), 48), 1.2f + i * 0.15f));
            }
        }

        /// <summary>持续的小表现：睡着冒 z。</summary>
        void StepAmbient(PetState s)
        {
            if (s.Asleep && Time.unscaledTime >= _zzzAt)
            {
                _zzzAt = Time.unscaledTime + 1.4f;
                var head = ScreenToLogical(_view.ScreenBounds);
                _fx.Add(new Drift(_fxRoot, "z", 18, new Color(0.85f, 0.9f, 1f), new Vector2(head.center.x + head.width * 0.22f, head.yMax - 24), new Vector2(18, 30), 2.2f));
            }
        }

        void StepHud(float dt)
        {
            var box = PetBox;
            var character = ScreenToLogical(_view.ScreenBounds);
            var stats = new PetHud.Stats();
            var showStats = false;
            if (_core != null)
            {
                var s = _core.State;
                var p = s.Pet;
                var cfg = _core.Config;
                var excited = s.ComboTier == "epic";
                var starving = p.Hunger >= cfg.HungryAt && !excited && !s.Asleep;
                stats = new PetHud.Stats
                {
                    Title = p.Name + " · " + _core.LookLabel + " · Lv." + p.Level + (s.Prestige > 0 ? " · " + (s.Prestige + 1) + " 世" : ""),
                    Hunger = p.Hunger,
                    Hungry = p.Hunger >= cfg.HungryAt,
                    Mood = p.Mood,
                    Energy = p.Energy,
                    Snacks = s.Snacks,
                    SnackMax = cfg.ManualSnackMax,
                    Combo = s.ComboCount,
                    Multiplier = s.ComboMultiplier,
                    ComboTier = s.ComboTier,
                    Starving = starving,
                    DimIcon = PetRules.MoodDimOf(_core.Data, p, cfg)?.Icon,
                };
                showStats = Time.unscaledTime < _hoverUntil || _menu.Open || _dragging;
            }
            _hud.Step(dt, character, box, stats, showStats);
            if (_menu.Open)
            {
                var cursor = ToLogical(Cursor());
                _menu.Refresh(_menu.Contains(cursor) ? cursor : (Vector2?)null);
            }
        }

        void StepGlow()
        {
            var frenzy = _core != null && _core.State.Buff != null;
            var c = _glow.color;
            c.a = Mathf.MoveTowards(c.a, frenzy ? 0.55f + 0.15f * Mathf.Sin(Time.time * 7) : 0, Time.unscaledDeltaTime * 2);
            _glow.color = c;
            _glow.enabled = c.a > 0.01f;
            if (!_glow.enabled) return;
            var r = _view.ScreenBounds;
            _glow.transform.localPosition = PetStageCamera.ScreenToWorld(r.center, 1);
            var size = Mathf.Max(r.width, r.height) * 1.5f / PetStageCamera.PixelsPerUnit;
            var pulse = 1 + 0.06f * Mathf.Sin(Time.time * 7);
            _glow.transform.localScale = Vector3.one * size * pulse / (_glow.sprite.bounds.size.x);
        }

        // ─── 鼠标 ───

        Vector2 Cursor()
        {
            return _windowReady ? DesktopWindow.CursorInWindow : (Vector2)Input.mousePosition;
        }

        bool Button(int b)
        {
            return _windowReady ? DesktopWindow.MouseDown(b) : Input.GetMouseButton(b);
        }

        List<PetMenu.Item> MenuItems()
        {
            var items = new List<PetMenu.Item>
            {
                new PetMenu.Item
                {
                    Icon = "🍬",
                    Label = () => "喂零食（还有 " + (_core?.State.Snacks ?? 0) + " 颗）",
                    Run = () => _core?.Snack(),
                    Enabled = () => _core != null && _core.State.Snacks > 0,
                },
                new PetMenu.Item { Icon = "💗", Label = () => "摸摸头", Run = () => _core?.Pat() },
                new PetMenu.Item
                {
                    Icon = "🔁",
                    Label = () => "换下一只（共 " + (_core?.State.PetCount ?? 1) + " 只）",
                    Run = () => _core?.SwitchPet(1),
                    Visible = () => _core != null && _core.State.PetCount > 1,
                },
            };
            items.Add(new PetMenu.Item { Icon = "📋", Label = () => PanelOpen ? "收起宠物面板" : "宠物面板", Run = () => PanelOpen = !PanelOpen });
            items.AddRange(_hostItems);
            items.Add(new PetMenu.Item { Label = () => "收起菜单", Run = () => { } });
            return items;
        }

        void HandleMouse()
        {
            var cursor = Cursor();
            var logical = ToLogical(cursor);
            var inWindow = cursor.x >= 0 && cursor.y >= 0 && cursor.x <= Screen.width && cursor.y <= Screen.height;
            var overPet = inWindow && _view.HitTest(cursor);
            var overMenu = _menu.Contains(logical);
            var overPanel = _panel.Contains(logical);
            var target = _miniGame.TargetAt(logical, PetBox);
            var left = Button(0);
            var right = Button(1);
            var leftDown = left && !_prevLeft;
            var leftUp = !left && _prevLeft;
            var rightDown = right && !_prevRight;
            _prevLeft = left;
            _prevRight = right;

            if (overPet) _hoverUntil = Time.unscaledTime + 1.5f;
            _view.LookAt(inWindow ? cursor : (Vector2?)null);

            // 只认「按下时就在宠物 / 菜单上」的点击：点击穿透时我们也看得到桌面上的每一次按键。
            if (leftDown)
            {
                if (_menu.Open)
                {
                    if (overMenu) _menu.Click(logical);
                    else _menu.Close();
                }
                else if (overPanel) _panel.Click(logical);
                else if (target >= 0) _core?.HitMiniGameTarget(target);
                else if (overPet)
                {
                    _pressOnPet = true;
                    _dragging = false;
                    _pressCursor = DesktopWindow.CursorScreen;
                    _pressWindow = DesktopWindow.WindowRect;
                }
            }
            if (rightDown)
            {
                if (overPet)
                {
                    _menu.Show(logical, PetBox, MenuItems());
                    if (Trace) StartCoroutine(CaptureMenu());
                }
                else if (_menu.Open && !overMenu) _menu.Close();
            }
            if (_pressOnPet && left)
            {
                var now = DesktopWindow.CursorScreen;
                var dx = now.x - _pressCursor.x;
                var dy = now.y - _pressCursor.y;
                if (!_dragging && Mathf.Abs(dx) + Mathf.Abs(dy) > 4 * _scale) _dragging = true;
                if (_dragging && _windowReady) DesktopWindow.Move(_pressWindow.Left + dx, _pressWindow.Top + dy);
            }
            if (_pressOnPet && leftUp)
            {
                if (!_dragging) _core?.Pat();
                else if (_windowReady)
                {
                    var r = DesktopWindow.WindowRect;
                    var clamped = WindowPlacement.Clamp(r.Left, r.Top, r.Width, r.Height);
                    if (clamped.x != r.Left || clamped.y != r.Top) DesktopWindow.Move(clamped.x, clamped.y);
                    if (!_debugMode) WindowPlacement.Save(clamped.x, clamped.y);
                }
                _pressOnPet = false;
                _dragging = false;
            }

            if (TraceMode == "menu" && !_demoMenuShown && Time.unscaledTime > 3)
            {
                _demoMenuShown = true;
                var head = ScreenToLogical(_view.ScreenBounds);
                _menu.Show(new Vector2(head.center.x, head.center.y), PetBox, MenuItems());
                StartCoroutine(CaptureMenu());
            }
            if (TraceMode == "panel" && !_demoMenuShown && Time.unscaledTime > 3)
            {
                _demoMenuShown = true;
                StartCoroutine(TourPanel());
            }
            if (Trace && Time.unscaledTime >= _traceAt)
            {
                _traceAt = Time.unscaledTime + 1;
                Debug.Log("[pet] cursor " + cursor + " screen " + DesktopWindow.CursorScreen + " window " + DesktopWindow.WindowRect + " size " + Screen.width + "×" + Screen.height
                    + " scale " + _scale + " ready " + _windowReady + " over " + overPet + " bounds " + _view.ScreenBounds + " centerHit " + _view.HitTest(_view.ScreenBounds.center) + " left " + left);
            }

            var interactive = overPet || overMenu || overPanel || target >= 0 || _pressOnPet || _debugMode;
            if (_windowReady) DesktopWindow.SetClickThrough(!interactive);
            if (interactive) _lastActivity = Time.unscaledTime;
        }

        /// <summary>诊断：菜单打开后把这一帧（带 alpha）存成 ~/.deepseek-pet/menu-capture.png，并把菜单的布局打进日志。</summary>
        IEnumerator TourPanel()
        {
            PanelOpen = true;
            for (var i = 0; i < _panel.TabCount; i++)
            {
                _panel.Tab = i;
                yield return new WaitForSecondsRealtime(1.2f);
                yield return Capture("trace-panel-" + i + ".png");
            }
            PanelOpen = false;
            yield return new WaitForSecondsRealtime(1f);
            _core?.StartMiniGame();
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Capture("trace-minigame.png");
        }

        IEnumerator Capture(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            tex.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(WindowPlacement.FilePath()), name), tex.EncodeToPNG());
            Destroy(tex);
        }

        IEnumerator CaptureMenu()
        {
            yield return new WaitForEndOfFrame();
            var shot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
            shot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            shot.Apply();
            var path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(WindowPlacement.FilePath()), "menu-capture.png");
            System.IO.File.WriteAllBytes(path, shot.EncodeToPNG());
            Destroy(shot);
            Debug.Log("[pet] menu " + _menu.Bounds + " box " + PetBox + " → " + path);
        }

        /// <summary>托盘菜单：显示 / 隐藏、面板，再加上宿主给右键菜单的那几项（调试面板、退出）。</summary>
        List<TrayIcon.Item> TrayItems()
        {
            var items = new List<TrayIcon.Item>
            {
                new TrayIcon.Item { Label = () => DesktopWindow.Visible ? "隐藏桌宠" : "显示桌宠", Run = () => DesktopWindow.SetVisible(!DesktopWindow.Visible) },
                new TrayIcon.Item
                {
                    Label = () => "宠物面板",
                    Checked = () => PanelOpen,
                    Run = () =>
                    {
                        DesktopWindow.SetVisible(true);
                        PanelOpen = !PanelOpen;
                    },
                },
            };
            foreach (var host in _hostItems)
            {
                var item = host;
                items.Add(new TrayIcon.Item { Label = item.Label, Run = item.Run });
            }
            return items;
        }

        void OnApplicationQuit()
        {
            TrayIcon.Remove();
        }

        void OnDestroy()
        {
            TrayIcon.Remove();
        }

        // ─── 窗口 ───

        void StepWindow()
        {
            var busy = _fx.Count > 0 || _hud.BubbleVisible || _hud.StatsVisible || _menu.Open || _panel.Open || _miniGame.Active || _pressOnPet || _debugMode
                || (_core != null && _core.State.Buff != null) || Time.unscaledTime - _lastActivity < 6;
            Application.targetFrameRate = busy ? 30 : 12;
            if (_windowReady && !_pressOnPet && Time.unscaledTime >= _topmostAt)
            {
                _topmostAt = Time.unscaledTime + 3;
                DesktopWindow.KeepOnTop();
            }
        }
    }
}
