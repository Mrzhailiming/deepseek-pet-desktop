using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace DeepseekPet.Presentation
{
    /// <summary>
    /// 彩色 emoji：Resources/Emoji/{码点}.png（Twemoji，PetTools fetch-emoji 生成）。
    /// 文件名规则和 PetTools 的 EmojiFetcher.FileNameOf 相同：码点小写十六进制用「-」连，没有 ZWJ 时去掉 U+FE0F。
    /// 找不到图（不是 emoji，或者没导出）就返回 null，调用方退回文字。
    /// </summary>
    public static class EmojiSprites
    {
        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static string FileNameOf(string emoji)
        {
            var points = new List<int>();
            for (var i = 0; i < emoji.Length; i += char.IsSurrogatePair(emoji, i) ? 2 : 1) points.Add(char.ConvertToUtf32(emoji, i));
            if (!points.Contains(0x200D)) points.RemoveAll(p => p == 0xFE0F);
            var parts = new string[points.Count];
            for (var i = 0; i < points.Count; i++) parts[i] = points[i].ToString("x", CultureInfo.InvariantCulture);
            return string.Join("-", parts);
        }

        public static Sprite Get(string emoji)
        {
            if (string.IsNullOrEmpty(emoji)) return null;
            if (Cache.TryGetValue(emoji, out var sprite)) return sprite;
            sprite = Resources.Load<Sprite>("Emoji/" + FileNameOf(emoji));
            Cache[emoji] = sprite;
            return sprite;
        }
    }

    /// <summary>
    /// 「图标 + 文字」一行：图标有彩色图就画图，没有就把图标当文字拼在前面。整行以 Root 的中心对齐。
    /// </summary>
    sealed class IconText
    {
        public readonly RectTransform Root;
        readonly Image _icon;
        readonly Text _text;
        readonly float _iconSize;
        readonly TextAnchor _align;
        Color _color;

        public IconText(string name, Transform parent, int fontSize, Color color, TextAnchor align = TextAnchor.MiddleCenter, bool outline = true)
        {
            Root = UiKit.Node(name, parent);
            Root.sizeDelta = Vector2.zero;
            _align = align;
            _iconSize = fontSize * 1.25f;
            _icon = UiKit.Box("Icon", Root, null, Color.white);
            _icon.rectTransform.sizeDelta = new Vector2(_iconSize, _iconSize);
            _icon.preserveAspect = true;
            _text = UiKit.Label("Text", Root, fontSize, color, TextAnchor.MiddleLeft);
            _text.rectTransform.pivot = new Vector2(0, 0.5f);
            if (outline) UiKit.Outline(_text, new Color(0.08f, 0.1f, 0.2f, 0.85f), 1.5f);
            _color = color;
        }

        public Text Text => _text;

        public void Set(string icon, string text)
        {
            var sprite = EmojiSprites.Get(icon);
            _icon.gameObject.SetActive(sprite != null);
            _icon.sprite = sprite;
            _text.text = sprite != null || string.IsNullOrEmpty(icon) ? text : icon + " " + text;
            Layout();
        }

        void Layout()
        {
            var hasIcon = _icon.gameObject.activeSelf;
            var textWidth = string.IsNullOrEmpty(_text.text) ? 0 : _text.preferredWidth;
            var gap = hasIcon && textWidth > 0 ? 4 : 0;
            var total = (hasIcon ? _iconSize : 0) + gap + textWidth;
            var left = _align == TextAnchor.MiddleLeft ? 0 : -total / 2;
            _icon.rectTransform.anchoredPosition = new Vector2(left + _iconSize / 2, 0);
            _text.rectTransform.anchoredPosition = new Vector2(left + (hasIcon ? _iconSize + gap : 0), 0);
        }

        /// <summary>整行的颜色和透明度（图标只跟着变透明，不染色）。</summary>
        public void SetColor(Color color)
        {
            _color = color;
            _text.color = color;
            _icon.color = new Color(1, 1, 1, color.a);
        }

        public void SetFontSize(int size)
        {
            if (_text.fontSize == size) return;
            _text.fontSize = size;
            Layout();
        }

        public Color Color => _color;
    }
}
