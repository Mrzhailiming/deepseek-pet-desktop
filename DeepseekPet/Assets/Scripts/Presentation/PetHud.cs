using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DeepseekPet.Presentation
{
    /// <summary>
    /// 跟着角色走的那一圈 UI：头顶的台词气泡、脚下的状态条（悬停时出现）、连击徽标、饿了的提示、情绪三维的小图标。
    /// 坐标是画布的逻辑像素（原点左下）；PetPresenter 每帧把角色的屏幕范围换算好传进来。
    /// </summary>
    sealed class PetHud
    {
        const float BubbleMaxWidth = 230;

        readonly RectTransform _root;

        // 气泡
        readonly Image _bubble;
        readonly Image _tail;
        readonly Text _bubbleText;
        string _bubbleKind;
        float _bubbleAge = -1;

        // 状态条
        readonly CanvasGroup _stats;
        readonly Text _title;
        readonly Bar _hunger;
        readonly Bar _mood;
        readonly Bar _energy;
        readonly IconText _snacks;
        float _statsAlpha;

        // 徽标
        readonly Text _combo;
        readonly Text _hungry;
        readonly Image _dim;
        readonly Text _dimText;

        sealed class Bar
        {
            public Image Back;
            public Image Fill;

            public void Set(float value, Color color)
            {
                var w = 54 * Mathf.Clamp01(value / 100f);
                Fill.rectTransform.sizeDelta = new Vector2(Mathf.Max(6, w), 7);
                Fill.color = color;
            }
        }

        public PetHud(RectTransform parent)
        {
            _root = UiKit.Node("Hud", parent);
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = _root.offsetMax = Vector2.zero;

            _tail = UiKit.Box("BubbleTail", _root, null, UiKit.Colors.Paper);
            _tail.rectTransform.sizeDelta = new Vector2(12, 12);
            _tail.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            _bubble = UiKit.Box("Bubble", _root, UiKit.Rounded, UiKit.Colors.Paper);
            _bubble.rectTransform.pivot = new Vector2(0.5f, 0);
            _bubbleText = UiKit.Label("BubbleText", _bubble.rectTransform, 15, UiKit.Colors.Ink);
            _bubbleText.rectTransform.anchorMin = Vector2.zero;
            _bubbleText.rectTransform.anchorMax = Vector2.one;
            _bubbleText.rectTransform.offsetMin = new Vector2(12, 8);
            _bubbleText.rectTransform.offsetMax = new Vector2(-12, -8);
            _bubble.gameObject.SetActive(false);
            _tail.gameObject.SetActive(false);

            var stats = UiKit.Box("Stats", _root, UiKit.Rounded, new Color(0.08f, 0.1f, 0.2f, 0.72f));
            stats.rectTransform.pivot = new Vector2(0.5f, 0);
            stats.rectTransform.sizeDelta = new Vector2(220, 50);
            _stats = stats.gameObject.AddComponent<CanvasGroup>();
            _stats.alpha = 0;
            _title = UiKit.Label("Title", stats.rectTransform, 13, Color.white);
            _title.rectTransform.anchoredPosition = new Vector2(110, 37);
            _hunger = NewBar(stats.rectTransform, "🍖", 8);
            _mood = NewBar(stats.rectTransform, "💗", 78);
            _energy = NewBar(stats.rectTransform, "⚡", 148);
            _snacks = new IconText("Snacks", stats.rectTransform, 11, new Color(1, 1, 1, 0.85f), TextAnchor.MiddleCenter, false);
            _snacks.Root.anchoredPosition = new Vector2(110, 7);

            _combo = UiKit.Label("Combo", _root, 16, UiKit.Colors.Gold, TextAnchor.MiddleLeft);
            _combo.fontStyle = FontStyle.Bold;
            UiKit.Outline(_combo, UiKit.Colors.Shadow, 1.6f);
            _hungry = UiKit.Label("Hungry", _root, 14, UiKit.Colors.Hungry);
            _hungry.fontStyle = FontStyle.Bold;
            _hungry.text = "饿了…";
            UiKit.Outline(_hungry, UiKit.Colors.Shadow, 1.4f);
            _dim = UiKit.Box("Dim", _root, null, Color.white);
            _dim.preserveAspect = true;
            _dim.rectTransform.sizeDelta = new Vector2(26, 26);
            _dimText = UiKit.Label("DimText", _dim.rectTransform, 22, Color.white);
            _dimText.rectTransform.anchoredPosition = new Vector2(13, 13);
        }

        static Bar NewBar(RectTransform parent, string icon, float x)
        {
            var bar = new Bar();
            var sprite = EmojiSprites.Get(icon);
            if (sprite != null)
            {
                var image = UiKit.Box("Icon", parent, sprite, Color.white);
                image.preserveAspect = true;
                image.rectTransform.sizeDelta = new Vector2(13, 13);
                image.rectTransform.anchoredPosition = new Vector2(x + 6, 21);
            }
            else
            {
                var label = UiKit.Label("Icon", parent, 11, Color.white);
                label.text = icon;
                label.rectTransform.anchoredPosition = new Vector2(x + 6, 21);
            }
            bar.Back = UiKit.Box("Back", parent, UiKit.Rounded, new Color(1, 1, 1, 0.18f));
            bar.Back.type = Image.Type.Simple;
            bar.Back.rectTransform.pivot = new Vector2(0, 0.5f);
            bar.Back.rectTransform.sizeDelta = new Vector2(54, 7);
            bar.Back.rectTransform.anchoredPosition = new Vector2(x + 14, 21);
            bar.Fill = UiKit.Box("Fill", parent, UiKit.Rounded, Color.white);
            bar.Fill.type = Image.Type.Simple;
            bar.Fill.rectTransform.pivot = new Vector2(0, 0.5f);
            bar.Fill.rectTransform.anchoredPosition = new Vector2(x + 14, 21);
            return bar;
        }

        public bool BubbleVisible => _bubble.gameObject.activeSelf;

        public void ShowBubble(string kind, string text)
        {
            _bubbleKind = kind;
            _bubbleText.text = text;
            _bubbleText.horizontalOverflow = HorizontalWrapMode.Overflow;
            var width = Mathf.Min(BubbleMaxWidth, _bubbleText.preferredWidth + 26);
            _bubbleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _bubble.rectTransform.sizeDelta = new Vector2(width, 40);
            var height = Mathf.Max(34, LayoutHeight(width - 24) + 16);
            _bubble.rectTransform.sizeDelta = new Vector2(width, height);
            // 提示（真有用的那句）换个底色，和日常台词区分开。
            var paper = kind == "advice" ? new Color(0.88f, 0.93f, 1f, 0.96f) : UiKit.Colors.Paper;
            _bubble.color = paper;
            _tail.color = paper;
            _bubble.gameObject.SetActive(true);
            _tail.gameObject.SetActive(true);
            _bubbleAge = 0;
        }

        float LayoutHeight(float width)
        {
            var settings = _bubbleText.GetGenerationSettings(new Vector2(width, 0));
            return _bubbleText.cachedTextGeneratorForLayout.GetPreferredHeight(_bubbleText.text, settings) / _bubbleText.pixelsPerUnit;
        }

        public void HideBubble()
        {
            _bubble.gameObject.SetActive(false);
            _tail.gameObject.SetActive(false);
            _bubbleAge = -1;
        }

        public struct Stats
        {
            public string Title;
            public int Hunger;
            public bool Hungry;
            public int Mood;
            public int Energy;
            public int Snacks;
            public int SnackMax;
            public int Combo;
            public double Multiplier;
            public string ComboTier;
            public bool Starving;
            public string DimIcon;
        }

        /// <summary>每帧摆一次位置、刷一次内容。character 是角色的屏幕范围（逻辑像素），box 是整只宠物占的范围。</summary>
        public void Step(float dt, Rect character, Rect box, Stats stats, bool showStats)
        {
            // 气泡：头顶，水平居中、钳在窗口里。
            if (_bubbleAge >= 0)
            {
                _bubbleAge += dt;
                var size = _bubble.rectTransform.sizeDelta;
                var x = Mathf.Clamp(character.center.x, box.xMin + size.x / 2 + 4, box.xMax - size.x / 2 - 4);
                var y = Mathf.Min(character.yMax + 10, box.yMax - size.y - 4);
                _bubble.rectTransform.anchoredPosition = new Vector2(x, y);
                var pop = _bubbleAge < 0.16f ? Mathf.Lerp(0.82f, 1f, _bubbleAge / 0.16f) : 1f;
                _bubble.rectTransform.localScale = Vector3.one * pop;
                _tail.rectTransform.anchoredPosition = new Vector2(Mathf.Clamp(character.center.x, x - size.x / 2 + 14, x + size.x / 2 - 14), y + 1);
            }

            // 状态条：脚下。
            _statsAlpha = Mathf.MoveTowards(_statsAlpha, showStats ? 1 : 0, dt * 5);
            _stats.alpha = _statsAlpha;
            var statsRt = (RectTransform)_stats.transform;
            statsRt.anchoredPosition = new Vector2(Mathf.Clamp(character.center.x, box.xMin + 112, box.xMax - 112), box.yMin + 4);
            if (_statsAlpha > 0)
            {
                _title.text = stats.Title;
                var hungerColor = stats.Hungry ? Color.Lerp(UiKit.Colors.Hungry, Color.white, 0.5f + 0.5f * Mathf.Sin(Time.time * 6)) : (Color)new Color32(0xff, 0xb3, 0x5c, 0xff);
                _hunger.Set(100 - stats.Hunger, hungerColor);
                _mood.Set(stats.Mood, UiKit.Colors.Mood);
                _energy.Set(stats.Energy, UiKit.Colors.Energy);
                _snacks.Set("🍬", stats.Snacks + "/" + stats.SnackMax);
            }

            // 连击：右肩。
            _combo.gameObject.SetActive(stats.Combo >= 2);
            if (stats.Combo >= 2)
            {
                _combo.text = "×" + stats.Multiplier.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "  " + stats.Combo + " 连";
                _combo.color = stats.ComboTier == "epic" ? Color.HSVToRGB(Mathf.Repeat(Time.time * 0.7f, 1), 0.6f, 1) : stats.ComboTier == "gold" ? UiKit.Colors.Gold : Color.white;
                _combo.fontSize = stats.ComboTier == "epic" ? 19 : stats.ComboTier == "gold" ? 17 : 15;
                _combo.rectTransform.anchoredPosition = new Vector2(Mathf.Min(character.xMax - 10, box.xMax - 70), character.yMax - 26);
            }

            // 饿了：左肩，一跳一跳。
            _hungry.gameObject.SetActive(stats.Starving && !showStats);
            if (stats.Starving)
            {
                _hungry.rectTransform.anchoredPosition = new Vector2(Mathf.Max(character.xMin + 18, box.xMin + 30), character.yMax - 30 + Mathf.Abs(Mathf.Sin(Time.time * 4)) * 6);
            }

            // 情绪三维：左上角一个小图标。
            _dim.gameObject.SetActive(!string.IsNullOrEmpty(stats.DimIcon));
            if (!string.IsNullOrEmpty(stats.DimIcon))
            {
                var sprite = EmojiSprites.Get(stats.DimIcon);
                _dim.sprite = sprite;
                _dim.color = sprite != null ? Color.white : Color.clear;
                _dimText.text = sprite != null ? "" : stats.DimIcon;
                _dim.rectTransform.anchoredPosition = new Vector2(Mathf.Max(character.xMin + 12, box.xMin + 16), character.yMax - 4 + Mathf.Sin(Time.time * 2) * 2);
            }
        }

        public bool StatsVisible => _statsAlpha > 0.01f;
    }

    /// <summary>右键菜单：一列文字按钮。不走 EventSystem（窗口不抢焦点时 EventSystem 会忽略输入），命中自己算。</summary>
    sealed class PetMenu
    {
        public sealed class Item
        {
            /// <summary>行首的图标（emoji）；没有就 null。</summary>
            public string Icon;
            public Func<string> Label;
            public Action Run;
            public Func<bool> Enabled;
            public Func<bool> Visible;
        }

        const float MinWidth = 150;
        const float RowHeight = 28;
        const float PadX = 14;
        /// <summary>窗口底部给状态条留的高度（状态条在 y 4..54）。</summary>
        const float StatsReserve = 60;

        float _width = MinWidth;

        readonly RectTransform _root;
        readonly Image _panel;
        readonly List<IconText> _rows = new List<IconText>();
        readonly List<Image> _highlights = new List<Image>();
        List<Item> _shown = new List<Item>();

        public PetMenu(RectTransform parent)
        {
            _root = UiKit.Node("Menu", parent);
            _root.pivot = new Vector2(0, 1);
            // 根节点缩成一个点：面板挂在它的锚点上。Node 默认 100×100，面板会被挂到这 100×100 的左下角，
            // 画出来比 Bounds / 命中算的低整整 100 像素（悬停高亮错行、靠窗口底部时下面几行画到窗口外）。
            _root.sizeDelta = Vector2.zero;
            _panel = UiKit.Box("Panel", _root, UiKit.Rounded, new Color(0.1f, 0.12f, 0.22f, 0.94f));
            _panel.rectTransform.pivot = new Vector2(0, 1);
            _panel.rectTransform.anchoredPosition = Vector2.zero;
            _root.gameObject.SetActive(false);
        }

        public bool Open => _root.gameObject.activeSelf;

        public Rect Bounds
        {
            get
            {
                var size = _panel.rectTransform.sizeDelta;
                var p = _root.anchoredPosition;
                return new Rect(p.x, p.y - size.y, size.x, size.y);
            }
        }

        public void Show(Vector2 at, Rect box, List<Item> items)
        {
            _shown = items.FindAll(i => i.Visible == null || i.Visible());
            while (_rows.Count < _shown.Count)
            {
                var hl = UiKit.Box("Highlight", _panel.rectTransform, UiKit.Rounded, new Color(1, 1, 1, 0.14f));
                // 行从面板顶上往下排，所以锚在左上角（UiKit.Node 默认锚在左下角）。
                hl.rectTransform.anchorMin = hl.rectTransform.anchorMax = new Vector2(0, 1);
                hl.rectTransform.pivot = new Vector2(0, 1);
                _highlights.Add(hl);
                var row = new IconText("Row", _panel.rectTransform, 14, Color.white, TextAnchor.MiddleLeft, false);
                row.Root.anchorMin = row.Root.anchorMax = new Vector2(0, 1);
                _rows.Add(row);
            }
            for (var i = 0; i < _rows.Count; i++)
            {
                var visible = i < _shown.Count;
                _rows[i].Root.gameObject.SetActive(visible);
                _highlights[i].gameObject.SetActive(false);
                if (!visible) continue;
                _rows[i].Root.anchoredPosition = new Vector2(PadX, -8 - RowHeight * i - RowHeight / 2);
                _highlights[i].rectTransform.anchoredPosition = new Vector2(4, -7 - RowHeight * i);
            }
            // 宽度按最长的那一行量（文字要先填进去才量得出来），但别超出宠物框。
            var widest = 0f;
            for (var i = 0; i < _shown.Count; i++)
            {
                _rows[i].Set(_shown[i].Icon, _shown[i].Label());
                widest = Mathf.Max(widest, _rows[i].Text.preferredWidth + (EmojiSprites.Get(_shown[i].Icon) != null ? 22 : 0));
            }
            _width = Mathf.Clamp(widest + PadX * 2, MinWidth, box.width - 8);
            for (var i = 0; i < _shown.Count; i++) _highlights[i].rectTransform.sizeDelta = new Vector2(_width - 8, RowHeight - 2);
            var height = 16 + RowHeight * _shown.Count;
            _panel.rectTransform.sizeDelta = new Vector2(_width, height);
            var x = Mathf.Clamp(at.x, box.xMin + 4, box.xMax - _width - 4);
            // 底边留出状态条那一截（菜单打开时状态条也在）：两块深色底叠在一起，下面几行就看不清了。
            var y = Mathf.Clamp(at.y, box.yMin + StatsReserve + height, box.yMax - 4);
            _root.anchoredPosition = new Vector2(x, y);
            _root.gameObject.SetActive(true);
            Refresh(null);
        }

        public void Close()
        {
            _root.gameObject.SetActive(false);
        }

        public bool Contains(Vector2 point)
        {
            return Open && Bounds.Contains(point);
        }

        int RowAt(Vector2 point)
        {
            if (!Contains(point)) return -1;
            var top = _root.anchoredPosition.y - 8;
            var i = Mathf.FloorToInt((top - point.y) / RowHeight);
            return i >= 0 && i < _shown.Count ? i : -1;
        }

        /// <summary>刷新文字和悬停高亮（cursor 为 null 表示不在菜单上）。</summary>
        public void Refresh(Vector2? cursor)
        {
            if (!Open) return;
            var hover = cursor.HasValue ? RowAt(cursor.Value) : -1;
            for (var i = 0; i < _shown.Count; i++)
            {
                var item = _shown[i];
                var enabled = item.Enabled == null || item.Enabled();
                _rows[i].Set(item.Icon, item.Label());
                _rows[i].SetColor(enabled ? Color.white : new Color(1, 1, 1, 0.4f));
                _highlights[i].gameObject.SetActive(i == hover && enabled);
            }
        }

        /// <summary>点了菜单里的这个点：执行那一项并收起菜单。</summary>
        public void Click(Vector2 point)
        {
            var i = RowAt(point);
            if (i < 0) return;
            var item = _shown[i];
            if (item.Enabled != null && !item.Enabled()) return;
            Close();
            item.Run();
        }
    }
}
