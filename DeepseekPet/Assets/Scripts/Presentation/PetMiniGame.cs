using System;
using System.Collections.Generic;
using DeepseekPet.Core;
using UnityEngine;
using UnityEngine.UI;

namespace DeepseekPet.Presentation
{
    /// <summary>
    /// 小游戏的画面（规则在 PetCore 里）：几样食物同时摆在宠物框里（位置是框宽 / 框高的百分比，和网页版一样），
    /// 要按顺序点中当前那一个；当前那个一跳一跳，点到的变淡打勾。过了时限没点就判输（网页版也是到点调 failMiniGame）。
    /// </summary>
    sealed class PetMiniGame
    {
        const float TargetSize = 38;

        readonly RectTransform _root;
        readonly Image _timer;
        readonly Text _result;
        readonly List<Image> _targets = new List<Image>();
        readonly List<Text> _fallback = new List<Text>();
        MiniGame _shown;
        bool _failSent;

        public PetMiniGame(RectTransform parent)
        {
            _root = UiKit.Node("MiniGame", parent);
            _root.sizeDelta = Vector2.zero;
            _timer = UiKit.Box("Timer", _root, UiKit.Rounded, UiKit.Colors.Gold);
            _timer.type = Image.Type.Simple;
            _timer.rectTransform.pivot = new Vector2(0, 0.5f);
            _result = UiKit.Label("Result", _root, 22, Color.white);
            _result.fontStyle = FontStyle.Bold;
            UiKit.Outline(_result, UiKit.Colors.Shadow, 2f);
            _root.gameObject.SetActive(false);
        }

        public bool Active => _shown != null;

        static Vector2 PositionOf(MiniGameTarget t, Rect box)
        {
            return new Vector2(box.xMin + box.width * t.X / 100f + TargetSize / 2, box.yMax - box.height * t.Y / 100f - TargetSize / 2);
        }

        /// <summary>这个点落在哪个目标上（逻辑像素）；没有返回 -1。</summary>
        public int TargetAt(Vector2 point, Rect box)
        {
            if (_shown == null || _shown.Finished) return -1;
            foreach (var t in _shown.Targets)
            {
                var c = PositionOf(t, box);
                if (Mathf.Abs(point.x - c.x) <= TargetSize / 2 + 4 && Mathf.Abs(point.y - c.y) <= TargetSize / 2 + 4) return t.Id;
            }
            return -1;
        }

        public void Step(PetCore core, Rect box)
        {
            var game = core?.State.MiniGame;
            if (game == null)
            {
                _shown = null;
                _failSent = false;
                _root.gameObject.SetActive(false);
                return;
            }
            if (_shown == null || game.Targets.Count != _targets.Count) Build(game);
            _shown = game;
            _root.gameObject.SetActive(true);

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (!game.Finished && now > game.Deadline && !_failSent)
            {
                _failSent = true;
                core.FailMiniGame();
                return;
            }
            for (var i = 0; i < game.Targets.Count; i++)
            {
                var t = game.Targets[i];
                var pos = PositionOf(t, box);
                var current = !game.Finished && t.Id == game.Current;
                var scale = current ? 1.1f + 0.12f * Mathf.Sin(Time.unscaledTime * 10) : 0.9f;
                var alpha = t.Caught ? 0.3f : current ? 1f : 0.65f;
                var node = _targets[i] != null ? _targets[i].rectTransform : _fallback[i].rectTransform;
                node.anchoredPosition = pos;
                node.localScale = Vector3.one * scale;
                if (_targets[i] != null) _targets[i].color = new Color(1, 1, 1, alpha);
                else _fallback[i].color = new Color(1, 1, 1, alpha);
            }
            // 倒计时条：宠物框顶上。
            var total = Math.Max(1, game.Deadline - game.StartedAt);
            var left = game.Finished ? 0 : Mathf.Clamp01((game.Deadline - now) / (float)Math.Max(1, Math.Min(total, core.Config.MiniGameTimeoutMs)));
            _timer.gameObject.SetActive(!game.Finished);
            _timer.rectTransform.anchoredPosition = new Vector2(box.xMin + 30, box.yMax - 10);
            _timer.rectTransform.sizeDelta = new Vector2((box.width - 60) * left, 6);
            _timer.color = left < 0.3f ? UiKit.Colors.Hungry : UiKit.Colors.Gold;
            _result.gameObject.SetActive(game.Finished);
            if (game.Finished)
            {
                _result.text = game.Success ? "通关！+" + core.Config.MiniGameExpReward + " 经验" : "下次加油";
                _result.color = game.Success ? UiKit.Colors.Gold : Color.white;
                _result.rectTransform.anchoredPosition = new Vector2(box.center.x, box.yMax - 70);
            }
        }

        void Build(MiniGame game)
        {
            foreach (var t in _targets) if (t != null) UnityEngine.Object.Destroy(t.gameObject);
            foreach (var t in _fallback) if (t != null) UnityEngine.Object.Destroy(t.gameObject);
            _targets.Clear();
            _fallback.Clear();
            foreach (var t in game.Targets)
            {
                var sprite = EmojiSprites.Get(t.Icon);
                if (sprite != null)
                {
                    var image = UiKit.Box("Target", _root, sprite, Color.white);
                    image.preserveAspect = true;
                    image.rectTransform.sizeDelta = new Vector2(TargetSize, TargetSize);
                    _targets.Add(image);
                    _fallback.Add(null);
                }
                else
                {
                    var text = UiKit.Label("Target", _root, 30, Color.white);
                    text.text = t.Icon;
                    _targets.Add(null);
                    _fallback.Add(text);
                }
            }
        }
    }
}
