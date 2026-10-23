using UnityEngine;
using UnityEngine.UI;

namespace DeepseekPet.Presentation
{
    /// <summary>
    /// 表现层的小工具：系统字体、程序生成的圆角框 / 圆点 / 光晕贴图、预乘 alpha 的 UI 材质，以及建 UI 节点的几个快捷方法。
    /// 不依赖任何美术资源 —— 立绘之外的东西都在这里画出来。
    /// </summary>
    public static class UiKit
    {
        static Font _font;
        static Material _material;
        static Sprite _rounded;
        static Sprite _circle;
        static Sprite _glow;

        /// <summary>
        /// 系统字体（中文 + emoji 兜底）。emoji 目前是单色字形：Unity 的旧版文字渲染画不了彩色字体。
        /// </summary>
        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    _font = Font.CreateDynamicFontFromOSFont(new[]
                    {
                        "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "Segoe UI Emoji", "Segoe UI Symbol", "Arial",
                    }, 32);
                }
                return _font;
            }
        }

        /// <summary>预乘 alpha 的 UI 材质（见 PetUIPremultiplied.shader）；找不到 shader 就退回默认材质。</summary>
        public static Material Material
        {
            get
            {
                if (_material == null)
                {
                    var shader = Shader.Find("DeepseekPet/UIPremultiplied");
                    _material = shader != null ? new Material(shader) : Graphic.defaultGraphicMaterial;
                }
                return _material;
            }
        }

        /// <summary>白色圆角矩形（9 宫格，边框 20px），着色后当气泡 / 菜单 / 进度条的底。</summary>
        public static Sprite Rounded
        {
            get
            {
                if (_rounded == null)
                {
                    const int size = 64;
                    const float radius = 18;
                    var tex = NewTexture(size);
                    var pixels = new Color32[size * size];
                    for (var y = 0; y < size; y++)
                    {
                        for (var x = 0; x < size; x++)
                        {
                            var dx = Mathf.Max(0, Mathf.Max(radius - (x + 0.5f), (x + 0.5f) - (size - radius)));
                            var dy = Mathf.Max(0, Mathf.Max(radius - (y + 0.5f), (y + 0.5f) - (size - radius)));
                            var d = Mathf.Sqrt(dx * dx + dy * dy);
                            var a = Mathf.Clamp01(radius - d + 0.5f);
                            pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                        }
                    }
                    tex.SetPixels32(pixels);
                    tex.Apply(false, true);
                    _rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, new Vector4(20, 20, 20, 20));
                }
                return _rounded;
            }
        }

        /// <summary>白色实心圆。</summary>
        public static Sprite Circle
        {
            get
            {
                if (_circle == null) _circle = Radial(64, r => Mathf.Clamp01((0.5f - r) * 64 + 0.5f));
                return _circle;
            }
        }

        /// <summary>白色柔光（中心实、边缘渐隐），暴食时垫在角色身后。</summary>
        public static Sprite Glow
        {
            get
            {
                if (_glow == null) _glow = Radial(128, r => Mathf.Pow(Mathf.Clamp01(1 - r * 2), 2.2f));
                return _glow;
            }
        }

        static Texture2D NewTexture(int size)
        {
            return new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
        }

        static Sprite Radial(int size, System.Func<float, float> alphaOfRadius)
        {
            var tex = NewTexture(size);
            var pixels = new Color32[size * size];
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var fx = (x + 0.5f) / size - 0.5f;
                    var fy = (y + 0.5f) / size - 0.5f;
                    var a = alphaOfRadius(Mathf.Sqrt(fx * fx + fy * fy));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100);
        }

        // ─── 建节点 ───

        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        public static Text Label(string name, Transform parent, int size, Color color, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var rt = Node(name, parent);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = size;
            text.color = color;
            text.alignment = anchor;
            text.material = Material;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        /// <summary>给文字描一圈深色边：桌面背景什么颜色都有，不描边的字在白底上看不见。</summary>
        public static void Outline(Text text, Color color, float width = 1.5f)
        {
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = color;
            outline.effectDistance = new Vector2(width, -width);
        }

        public static Image Box(string name, Transform parent, Sprite sprite, Color color)
        {
            var rt = Node(name, parent);
            var image = rt.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null && sprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.material = Material;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>颜色常量（和网页版样式表的配色对齐）。</summary>
        public static class Colors
        {
            public static readonly Color Ink = new Color32(0x1d, 0x23, 0x3b, 0xff);
            public static readonly Color Paper = new Color32(0xff, 0xff, 0xff, 0xf2);
            public static readonly Color Gold = new Color32(0xff, 0xc8, 0x3d, 0xff);
            public static readonly Color Epic = new Color32(0xff, 0x6b, 0xd6, 0xff);
            public static readonly Color Blue = new Color32(0x4d, 0x6b, 0xfe, 0xff);
            public static readonly Color Hungry = new Color32(0xff, 0x5a, 0x5a, 0xff);
            public static readonly Color Mood = new Color32(0xff, 0x9f, 0xc8, 0xff);
            public static readonly Color Energy = new Color32(0x6c, 0xd4, 0x8f, 0xff);
            public static readonly Color Shadow = new Color(0, 0, 0, 0.55f);
        }
    }
}
