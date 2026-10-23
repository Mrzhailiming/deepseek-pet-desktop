using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DeepseekPet.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DeepseekPet.Presentation
{
    /// <summary>
    /// 宠物面板：状态 / 技能 / 成就 / 任务 / 蛋 / 宠物 六页。从右键菜单打开，摆在宠物左边。
    ///
    /// 和菜单一样不走 EventSystem（窗口不抢焦点时它会忽略输入）：按钮的命中自己算。
    /// 内容整页重建 —— 每页也就几十个节点，状态一变（最多每秒 4 次）或换页时重画一遍，省得维护一堆增量更新。
    /// </summary>
    sealed class PetPanel
    {
        public const float Width = 340;
        public const float Height = 370;

        static readonly string[] Tabs = { "状态", "技能", "成就", "任务", "蛋", "宠物" };

        sealed class Button
        {
            public Rect Rect;
            public Action Run;
            public bool Enabled;
            public Image Back;
        }

        readonly RectTransform _root;
        readonly Image _panel;
        RectTransform _page;
        readonly List<Button> _buttons = new List<Button>();
        readonly List<Button> _tabButtons = new List<Button>();
        int _tab;
        float _rebuildAt;
        PetState _built;
        int _petPage;
        bool _prestigeArmed;
        float _prestigeArmedUntil;

        readonly Action _close;

        public PetPanel(RectTransform parent, Action close)
        {
            _close = close;
            _root = UiKit.Node("Panel", parent);
            _root.sizeDelta = Vector2.zero;
            _panel = UiKit.Box("Back", _root, UiKit.Rounded, new Color(0.09f, 0.11f, 0.2f, 0.95f));
            _panel.rectTransform.pivot = Vector2.zero;
            _panel.rectTransform.sizeDelta = new Vector2(Width, Height);
            _root.gameObject.SetActive(false);
        }

        public bool Open => _root.gameObject.activeSelf;

        public int TabCount => Tabs.Length;

        public int Tab
        {
            get => _tab;
            set
            {
                _tab = Mathf.Clamp(value, 0, Tabs.Length - 1);
                _built = null;
            }
        }

        public Rect Bounds => new Rect(_root.anchoredPosition, new Vector2(Width, Height));

        public void Show(Vector2 bottomLeft)
        {
            _root.anchoredPosition = bottomLeft;
            _root.gameObject.SetActive(true);
            _built = null;
        }

        public void Hide()
        {
            _root.gameObject.SetActive(false);
        }

        public void Move(Vector2 bottomLeft)
        {
            _root.anchoredPosition = bottomLeft;
        }

        public bool Contains(Vector2 point) => Open && Bounds.Contains(point);

        /// <summary>点了面板里的这个点（逻辑像素，画布坐标）。</summary>
        public void Click(Vector2 point)
        {
            var local = point - _root.anchoredPosition;
            foreach (var b in _tabButtons.Concat(_buttons).ToList())
            {
                if (!b.Enabled || !b.Rect.Contains(local)) continue;
                b.Run();
                _built = null;
                return;
            }
        }

        /// <summary>每帧：状态变了（限速）或换了页就重建；悬停的按钮亮一点。</summary>
        public void Step(PetCore core, Vector2 cursor)
        {
            if (!Open || core == null) return;
            var state = core.State;
            if (_prestigeArmed && Time.unscaledTime > _prestigeArmedUntil)
            {
                _prestigeArmed = false;
                _built = null;
            }
            if (_built == null || (state != _built && Time.unscaledTime >= _rebuildAt))
            {
                Rebuild(core);
                _built = state;
                _rebuildAt = Time.unscaledTime + 0.25f;
            }
            var local = cursor - _root.anchoredPosition;
            foreach (var b in _tabButtons.Concat(_buttons))
            {
                if (b.Back == null) continue;
                var hover = b.Enabled && b.Rect.Contains(local);
                var c = b.Back.color;
                c.a = !b.Enabled ? 0.06f : hover ? 0.32f : 0.16f;
                b.Back.color = c;
            }
        }

        // ─── 搭页面的小工具（坐标：面板左下角为原点，y 向上）───

        Text Label(string text, float x, float y, int size = 13, Color? color = null, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var t = UiKit.Label("Label", _page, size, color ?? Color.white, anchor);
            t.rectTransform.pivot = anchor == TextAnchor.MiddleLeft ? new Vector2(0, 0.5f) : anchor == TextAnchor.MiddleRight ? new Vector2(1, 0.5f) : new Vector2(0.5f, 0.5f);
            t.rectTransform.anchoredPosition = new Vector2(x, y);
            t.text = text;
            return t;
        }

        IconText IconLabel(string icon, string text, float x, float y, int size = 13, Color? color = null)
        {
            var line = new IconText("IconLabel", _page, size, color ?? Color.white, TextAnchor.MiddleLeft, false);
            line.Root.anchoredPosition = new Vector2(x, y);
            line.Set(icon, text);
            return line;
        }

        void Bar(float x, float y, float width, float value, float max, Color color)
        {
            var back = UiKit.Box("BarBack", _page, UiKit.Rounded, new Color(1, 1, 1, 0.14f));
            back.type = Image.Type.Simple;
            back.rectTransform.pivot = new Vector2(0, 0.5f);
            back.rectTransform.anchoredPosition = new Vector2(x, y);
            back.rectTransform.sizeDelta = new Vector2(width, 7);
            var fill = UiKit.Box("BarFill", _page, UiKit.Rounded, color);
            fill.type = Image.Type.Simple;
            fill.rectTransform.pivot = new Vector2(0, 0.5f);
            fill.rectTransform.anchoredPosition = new Vector2(x, y);
            fill.rectTransform.sizeDelta = new Vector2(Mathf.Max(5, width * Mathf.Clamp01(max <= 0 ? 0 : value / max)), 7);
        }

        Button AddButton(List<Button> into, string icon, string text, float x, float y, float width, bool enabled, Action run, float height = 24)
        {
            var back = UiKit.Box("Button", into == _tabButtons ? _root : (Transform)_page, UiKit.Rounded, new Color(1, 1, 1, 0.16f));
            back.rectTransform.pivot = Vector2.zero;
            back.rectTransform.anchoredPosition = new Vector2(x, y);
            back.rectTransform.sizeDelta = new Vector2(width, height);
            var line = new IconText("Text", back.rectTransform, 13, enabled ? Color.white : new Color(1, 1, 1, 0.4f), TextAnchor.MiddleCenter, false);
            line.Root.anchoredPosition = new Vector2(width / 2, height / 2);
            line.Set(icon, text);
            var b = new Button { Rect = new Rect(x, y, width, height), Run = run, Enabled = enabled, Back = back };
            into.Add(b);
            return b;
        }

        void Rebuild(PetCore core)
        {
            if (_page != null) UnityEngine.Object.Destroy(_page.gameObject);
            foreach (var b in _tabButtons) if (b.Back != null) UnityEngine.Object.Destroy(b.Back.gameObject);
            _tabButtons.Clear();
            _buttons.Clear();
            _page = UiKit.Node("Page", _root);
            _page.sizeDelta = Vector2.zero;

            // 页签
            var tabWidth = (Width - 16 - 28) / Tabs.Length;
            for (var i = 0; i < Tabs.Length; i++)
            {
                var index = i;
                var b = AddButton(_tabButtons, null, Tabs[i], 8 + i * tabWidth, Height - 32, tabWidth - 4, true, () => _tab = index);
                if (i == _tab) b.Back.color = new Color(0.3f, 0.42f, 1f, 0.5f);
            }

            AddButton(_tabButtons, null, "✕", Width - 34, Height - 32, 26, true, () => _close());
            switch (_tab)
            {
                case 0: BuildStatus(core); break;
                case 1: BuildSkills(core); break;
                case 2: BuildAchievements(core); break;
                case 3: BuildQuests(core); break;
                case 4: BuildEggs(core); break;
                case 5: BuildPets(core); break;
            }
            // 按钮背景画在页签之后会盖住页签的高亮：页签挪到最上层。
            foreach (var b in _tabButtons) b.Back.transform.SetAsLastSibling();
        }

        static string N(long v) => v.ToString(CultureInfo.InvariantCulture);

        // ─── 状态 ───

        void BuildStatus(PetCore core)
        {
            var s = core.State;
            var p = s.Pet;
            var cfg = core.Config;
            var data = core.Data;
            var y = Height - 56f;
            var title = PetTables.TitleOf(data, p.Level, s.Prestige, s.FormsOwned.Count);
            Label(p.Name + " · " + core.LookLabel + " · 「" + title + "」", 14, y, 15, UiKit.Colors.Gold);
            y -= 24;
            Label("Lv." + p.Level + (s.Prestige > 0 ? "   第 " + (s.Prestige + 1) + " 世（经验 +" + Math.Round(s.Prestige * cfg.PrestigeExpBonus * 100) + "%）" : "") + (string.IsNullOrEmpty(s.Talent) ? "" : "   天赋：" + s.Talent), 14, y);
            y -= 18;
            Bar(14, y, Width - 28, p.Exp, p.Level * data.ExpPerLevel, UiKit.Colors.Gold);
            Label(p.Exp + " / " + p.Level * data.ExpPerLevel + " 经验", Width - 14, y - 12, 11, new Color(1, 1, 1, 0.7f), TextAnchor.MiddleRight);
            y -= 30;
            void Stat(string icon, string name, int value, Color color, float x, float yy)
            {
                IconLabel(icon, name + " " + value, x, yy, 12);
                Bar(x + 74, yy, 66, value, 100, color);
            }
            Stat("🍖", "饱食", 100 - p.Hunger, p.Hunger >= cfg.HungryAt ? UiKit.Colors.Hungry : (Color)new Color32(0xff, 0xb3, 0x5c, 0xff), 14, y);
            Stat("💗", "心情", p.Mood, UiKit.Colors.Mood, 176, y);
            y -= 22;
            Stat("⚡", "精力", p.Energy, UiKit.Colors.Energy, 14, y);
            Stat("🤔", "好奇", p.Curiosity, new Color(0.6f, 0.8f, 1f), 176, y);
            y -= 22;
            Stat("😤", "得意", p.Pride, UiKit.Colors.Gold, 14, y);
            Stat("😟", "担忧", p.Concern, new Color(0.75f, 0.75f, 0.9f), 176, y);
            y -= 28;
            var together = PetRules.TogetherDaysOf(s.Memory, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), core.Zone);
            Label("累计 " + N(s.TotalFeeds) + " 口 · " + PetRules.FormatTokens(s.TotalTokens) + " tok · 相处 " + together + " 天 · 连续 " + s.StreakCount + " 天", 14, y, 12, new Color(1, 1, 1, 0.8f));
            y -= 18;
            Label("输入 " + PetRules.FormatTokens(s.TokensBySource.UserInput) + " · 生成 " + PetRules.FormatTokens(s.TokensBySource.Generation) + " · 工具 " + PetRules.FormatTokens(s.TokensBySource.ToolResult) + " · 零食 " + s.Snacks + "/" + cfg.ManualSnackMax, 14, y, 12, new Color(1, 1, 1, 0.8f));
            y -= 30;

            // 形态：收集过的都能换。
            if (s.FormsOwned.Count > 0)
            {
                Label("形态", 14, y, 12, new Color(1, 1, 1, 0.7f));
                var x = 50f;
                foreach (var key in s.FormsOwned)
                {
                    var form = PetRules.FormOf(data, key);
                    if (form == null) continue;
                    var current = p.Form == key;
                    AddButton(_buttons, form.Icon, form.Label, x, y - 12, 66, !current, () => core.SwitchForm(key));
                    x += 70;
                }
                y -= 34;
            }

            // 小游戏 / 轮回
            var canPrestige = cfg.PrestigeEnabled && p.Level >= cfg.PrestigeMinLevel;
            AddButton(_buttons, "🎮", "小游戏", 14, 14, 150, cfg.MiniGameEnabled && s.MiniGame == null, () => core.StartMiniGame(), 28);
            var prestigeText = !canPrestige ? "轮回（Lv." + cfg.PrestigeMinLevel + "）" : _prestigeArmed ? "再点一次确认" : "轮回";
            AddButton(_buttons, "🔄", prestigeText, Width - 164, 14, 150, canPrestige, () =>
            {
                // 轮回会把等级和技能等级清零：点两下才算数。
                if (!_prestigeArmed)
                {
                    _prestigeArmed = true;
                    _prestigeArmedUntil = Time.unscaledTime + 3;
                    return;
                }
                _prestigeArmed = false;
                core.Prestige();
            }, 28);
        }

        // ─── 技能 ───

        void BuildSkills(PetCore core)
        {
            var s = core.State;
            var cfg = core.Config;
            var y = Height - 60f;
            foreach (var item in core.Data.Skills)
            {
                var row = s.Skills[item.Key];
                if (row == null) continue;
                var stars = PetRules.MasteryStarsOf(row.Mastery, cfg);
                IconLabel(item.Icon, item.Label + "  Lv." + row.Level + (stars != "" ? "  " + stars : ""), 14, y, 14);
                var max = row.Level >= cfg.SkillMaxLevel;
                var need = PetRules.SkillNeedOf(row.Level, cfg);
                if (max)
                {
                    var tier = cfg.MasteryXpPerTier;
                    var into = tier > 0 ? row.Mastery % tier : 0;
                    Bar(14, y - 18, Width - 28, into, tier, UiKit.Colors.Gold);
                    Label("满级 · 精通 " + into + "/" + tier, Width - 14, y - 30, 11, new Color(1, 1, 1, 0.65f), TextAnchor.MiddleRight);
                }
                else
                {
                    Bar(14, y - 18, Width - 28, row.Xp, need, UiKit.Colors.Blue);
                    Label(row.Xp + " / " + need, Width - 14, y - 30, 11, new Color(1, 1, 1, 0.65f), TextAnchor.MiddleRight);
                }
                y -= 50;
            }
            var m = s.Memory;
            y -= 4;
            var files = m.Files.Take(4).Select(f => f.Name + "×" + f.Count);
            var tools = m.Tools.Take(5).Select(t => t.Name + "×" + t.Count);
            Label("常改：" + (m.Files.Count > 0 ? string.Join("  ", files) : "（还没有）"), 14, y, 11, new Color(1, 1, 1, 0.75f));
            Label("常用：" + (m.Tools.Count > 0 ? string.Join("  ", tools) : "（还没有）"), 14, y - 18, 11, new Color(1, 1, 1, 0.75f));
            var busy = PetRules.BusyHoursOf(m.Hours);
            Label("常在 " + (busy ?? "—") + " 点干活 · 报错 " + m.Errors + " 次 · 跨过 " + m.Recoveries + " 次", 14, y - 36, 11, new Color(1, 1, 1, 0.75f));
        }

        // ─── 成就 ───

        void BuildAchievements(PetCore core)
        {
            var s = core.State;
            var list = core.Data.Achievements;
            Label("已解锁 " + s.Achievements.Count + " / " + list.Count, 14, Height - 52, 12, UiKit.Colors.Gold);
            var colWidth = (Width - 28) / 2;
            for (var i = 0; i < list.Count; i++)
            {
                var a = list[i];
                var owned = s.Achievements.Contains(a.Id);
                var col = i % 2;
                var row = i / 2;
                var x = 14 + col * colWidth;
                var y = Height - 76 - row * 28;
                var line = IconLabel(owned ? a.Icon : "🔒", a.Label, x, y, 12, owned ? Color.white : new Color(1, 1, 1, 0.45f));
                if (!owned) Label(a.Hint, x + 24, y - 11, 9, new Color(1, 1, 1, 0.4f));
            }
        }

        // ─── 任务 ───

        void BuildQuests(PetCore core)
        {
            var s = core.State;
            var cfg = core.Config;
            var y = Height - 60f;
            Label("今日任务（每完成一个 +" + cfg.DailyQuestExp + " 经验、+" + cfg.DailyQuestSnacks + " 零食）", 14, y, 12, UiKit.Colors.Gold);
            y -= 30;
            foreach (var quest in PetRules.PickDailyQuests(core.Data, s.Daily.Day))
            {
                var done = s.Daily.Done.Contains(quest.Id);
                var have = Math.Min(quest.Progress(s.Daily), quest.Goal);
                IconLabel(done ? "✅" : quest.Icon, quest.Label, 14, y, 13, done ? new Color(0.7f, 1f, 0.75f) : Color.white);
                Bar(14, y - 18, Width - 28, have, quest.Goal, done ? UiKit.Colors.Energy : UiKit.Colors.Blue);
                Label(PetRules.FormatTokens(have) + " / " + PetRules.FormatTokens(quest.Goal), Width - 14, y - 30, 11, new Color(1, 1, 1, 0.65f), TextAnchor.MiddleRight);
                y -= 52;
            }
            y -= 6;
            var d = s.Daily;
            Label("今天：喂食 " + d.Feeds + " · 工具 " + d.Tools + " · 最高连击 " + d.BestCombo + " · 摸头 " + d.Pats + " · 零食 " + d.Snacks + " · " + PetRules.FormatTokens(d.Tokens) + " tok", 14, y, 11, new Color(1, 1, 1, 0.75f));
            Label("连续到访 " + s.StreakCount + " 天", 14, y - 18, 11, new Color(1, 1, 1, 0.75f));
        }

        // ─── 蛋 ───

        void BuildEggs(PetCore core)
        {
            var s = core.State;
            var eggs = core.EggList;
            var y = Height - 56f;
            Label("蛋 " + eggs.Count + " 颗 · 宠物 " + s.PetCount + " 只", 14, y, 12, UiKit.Colors.Gold);
            y -= 26;
            if (eggs.Count == 0)
            {
                Label("还没有蛋。累计 token、喂食次数、成就和孵化数到里程碑会给蛋，喂食时也有小概率掉一颗。", 14, y, 11, new Color(1, 1, 1, 0.65f));
                return;
            }
            // 同一种蛋叠在一起显示，孵的时候孵那一种里最早的一颗。
            foreach (var group in eggs.GroupBy(e => e.Type).Take(8))
            {
                core.Data.EggTypeByKey.TryGetValue(group.Key, out var type);
                var species = type == null ? "" : type.Species == "random" ? "随机种族" : core.Data.SpeciesByKey.TryGetValue(type.Species, out var sp) ? sp.Label : type.Species;
                IconLabel("🥚", (type?.Label ?? group.Key) + " ×" + group.Count() + "  → " + species, 14, y, 13);
                var first = group.First();
                AddButton(_buttons, null, "孵化", Width - 84, y - 12, 70, s.PetCount < core.Data.MaxPets, () =>
                {
                    core.StartHatch(first.Id);
                    core.ConfirmHatch(null);
                });
                y -= 32;
            }
        }

        // ─── 宠物 ───

        void BuildPets(PetCore core)
        {
            var s = core.State;
            const int perPage = 7;
            var order = s.PetOrder;
            var pages = Mathf.Max(1, (order.Count + perPage - 1) / perPage);
            _petPage = Mathf.Clamp(_petPage, 0, pages - 1);
            var y = Height - 58f;
            for (var i = _petPage * perPage; i < Math.Min(order.Count, (_petPage + 1) * perPage); i++)
            {
                var id = order[i];
                var rec = core.Collection[id];
                var active = id == s.ActivePetId;
                var fav = s.Favorites.Contains(id);
                // 当前这只的等级以状态为准（档案要到存档时才写回）。
                var level = active ? s.Pet.Level : rec.Pet.Level;
                var name = active ? s.Pet.Name : rec.Name;
                var icon = core.Data.SpeciesByKey.TryGetValue(rec.Species ?? "", out var sp) ? sp.Icon : rec.Icon;
                IconLabel(icon, (fav ? "★ " : "") + name + "  Lv." + level + (rec.Prestige > 0 ? " · " + (rec.Prestige + 1) + " 世" : ""), 14, y, 13, active ? UiKit.Colors.Gold : Color.white);
                if (active) AddButton(_buttons, null, fav ? "取消收藏" : "收藏", Width - 94, y - 12, 80, true, () => core.ToggleFavorite());
                else AddButton(_buttons, null, "换成它", Width - 94, y - 12, 80, true, () => core.SwitchPetTo(id));
                y -= 30;
            }
            if (pages > 1)
            {
                AddButton(_buttons, null, "◀", 14, 54, 40, _petPage > 0, () => _petPage--);
                Label((_petPage + 1) + " / " + pages, Width / 2, 66, 12, Color.white, TextAnchor.MiddleCenter);
                AddButton(_buttons, null, "▶", Width - 54, 54, 40, _petPage < pages - 1, () => _petPage++);
            }
            // 直接添加（不经过蛋）。
            Label("添加：", 14, 30, 12, new Color(1, 1, 1, 0.7f));
            var x = 56f;
            foreach (var species in core.Data.Species)
            {
                var key = species.Key;
                AddButton(_buttons, species.Icon, "", x, 18, 50, s.PetCount < core.Data.MaxPets, () => core.AddPet(key, null));
                x += 54;
            }
        }
    }
}
