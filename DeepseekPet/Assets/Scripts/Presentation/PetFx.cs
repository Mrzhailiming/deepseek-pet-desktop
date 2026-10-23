using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DeepseekPet.Presentation
{
    /// <summary>
    /// 一次性的小特效（UI 层，单位是画布的逻辑像素，原点左下）。每帧 Step，返回 false 就自己销毁。
    /// 不用 Animator / 粒子系统：这几样东西都是「从 A 飘到 B、慢慢变淡」，几行代码的事。
    /// </summary>
    abstract class Fx
    {
        protected RectTransform Node;
        protected float Age;
        protected float Life;

        /// <summary>推进 dt 秒；演完返回 false。</summary>
        public bool Step(float dt)
        {
            Age += dt;
            if (Node == null) return false;
            // 最后一帧也调一次 Update（p = 1），好让「到了」这种收尾有机会发生。
            var alive = Update(Mathf.Clamp01(Age / Life)) && Age < Life;
            if (!alive) Object.Destroy(Node.gameObject);
            return alive;
        }

        protected abstract bool Update(float p);

        public virtual void Kill()
        {
            if (Node != null) Object.Destroy(Node.gameObject);
            Node = null;
        }

        protected static float EaseOut(float p)
        {
            return 1 - (1 - p) * (1 - p);
        }

        protected static float EaseIn(float p)
        {
            return p * p;
        }
    }

    /// <summary>一口食物：从窗口边上飞进嘴里（二次贝塞尔，越飞越快），到了就回调。</summary>
    sealed class FoodFlight : Fx
    {
        readonly Vector2 _from;
        readonly Vector2 _via;
        readonly System.Func<Vector2> _to;
        readonly System.Action _arrive;
        readonly float _spin;

        public FoodFlight(Transform layer, string icon, int size, Color color, Vector2 from, System.Func<Vector2> to, System.Action arrive)
        {
            var sprite = EmojiSprites.Get(icon);
            if (sprite != null)
            {
                // 彩色 emoji 图：不染色（染了就不是那个颜色了）。
                var image = UiKit.Box("Food", layer, sprite, Color.white);
                image.preserveAspect = true;
                image.rectTransform.sizeDelta = new Vector2(size * 1.15f, size * 1.15f);
                Node = image.rectTransform;
            }
            else
            {
                var text = UiKit.Label("Food", layer, size, color);
                text.text = icon;
                UiKit.Outline(text, new Color(0, 0, 0, 0.45f), 1.2f);
                Node = text.rectTransform;
            }
            _from = from;
            _to = to;
            var end = to();
            _via = Vector2.Lerp(from, end, 0.45f) + new Vector2(0, 60 + Random.Range(0, 40));
            _arrive = arrive;
            _spin = Random.Range(-220f, 220f);
            Life = 0.6f;
            Node.anchoredPosition = from;
        }

        protected override bool Update(float p)
        {
            var e = EaseIn(p);
            var end = _to();
            var a = Vector2.Lerp(_from, _via, e);
            var b = Vector2.Lerp(_via, end, e);
            Node.anchoredPosition = Vector2.Lerp(a, b, e);
            Node.localRotation = Quaternion.Euler(0, 0, _spin * p);
            Node.localScale = Vector3.one * (1 - 0.35f * e);
            if (p >= 1)
            {
                _arrive?.Invoke();
                return false;
            }
            return true;
        }
    }

    /// <summary>飘字：从起点往上飘、放大一点再淡出。</summary>
    sealed class FloatingText : Fx
    {
        readonly IconText _line;
        readonly Vector2 _from;
        readonly float _rise;
        readonly Color _color;
        readonly bool _rainbow;

        /// <param name="icon">打头的图标（emoji，有彩色图就画图）；没有传 null。</param>
        public FloatingText(Transform layer, string icon, string text, int size, Color color, Vector2 from, float life, float rise = 46, bool rainbow = false)
        {
            _line = new IconText("Float", layer, size, color);
            _line.Text.fontStyle = FontStyle.Bold;
            _line.Set(icon, text);
            Node = _line.Root;
            Node.anchoredPosition = from;
            _from = from;
            _rise = rise;
            _color = color;
            _rainbow = rainbow;
            Life = life;
        }

        protected override bool Update(float p)
        {
            Node.anchoredPosition = _from + new Vector2(0, _rise * EaseOut(p));
            var pop = p < 0.15f ? Mathf.Lerp(0.6f, 1.12f, p / 0.15f) : Mathf.Lerp(1.12f, 1f, (p - 0.15f) / 0.85f);
            Node.localScale = Vector3.one * pop;
            var c = _rainbow ? Color.HSVToRGB(Mathf.Repeat(Time.time * 0.6f, 1), 0.55f, 1) : _color;
            c.a = p < 0.7f ? 1 : 1 - (p - 0.7f) / 0.3f;
            _line.SetColor(c);
            return true;
        }
    }

    /// <summary>漂走的小符号（摸头的爱心、睡觉的 z）：带一点左右摆动。</summary>
    sealed class Drift : Fx
    {
        readonly Text _text;
        readonly Vector2 _from;
        readonly Vector2 _velocity;
        readonly float _phase;
        readonly Color _color;

        public Drift(Transform layer, string symbol, int size, Color color, Vector2 from, Vector2 velocity, float life)
        {
            _text = UiKit.Label("Drift", layer, size, color);
            _text.text = symbol;
            UiKit.Outline(_text, new Color(0, 0, 0, 0.35f), 1f);
            Node = _text.rectTransform;
            Node.anchoredPosition = from;
            _from = from;
            _velocity = velocity;
            _phase = Random.Range(0, Mathf.PI * 2);
            _color = color;
            Life = life;
        }

        protected override bool Update(float p)
        {
            var t = Age;
            Node.anchoredPosition = _from + _velocity * t + new Vector2(Mathf.Sin(t * 5 + _phase) * 6, 0);
            Node.localScale = Vector3.one * (0.7f + 0.5f * p);
            var c = _color;
            c.a = 1 - p;
            _text.color = c;
            return true;
        }
    }

    /// <summary>一组特效：统一推进、统一清理。</summary>
    sealed class FxLayer
    {
        readonly List<Fx> _items = new List<Fx>();

        public int Count => _items.Count;

        public void Add(Fx fx)
        {
            _items.Add(fx);
            // 一大波工具循环时别把窗口塞满：老的先走。
            if (_items.Count > 40)
            {
                _items[0].Kill();
                _items.RemoveAt(0);
            }
        }

        public void Step(float dt)
        {
            for (var i = _items.Count - 1; i >= 0; i--)
            {
                if (!_items[i].Step(dt)) _items.RemoveAt(i);
            }
        }

        public void Clear()
        {
            foreach (var fx in _items) fx.Kill();
            _items.Clear();
        }
    }
}
