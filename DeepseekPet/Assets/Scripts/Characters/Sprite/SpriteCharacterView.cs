using System.Collections.Generic;
using DeepseekPet.Presentation;
using UnityEngine;

namespace DeepseekPet.Characters.Sprite
{
    /// <summary>
    /// 立绘角色，两套图：
    ///   - 鲸鱼娘立绘：4 档 × 7 种表情，Resources/WhaleSprites/deepseek-{档}-{表情}.png；
    ///   - 四种动物（代码猫 / 探索狐 / 调试虫 / 文鸟）：网页版矢量形态栅格出来的图（PetTools render-art），
    ///     Resources/FormSprites/{动物}-{表情}.png，另有三张情绪三维的脸 {动物}-normal-{维}.png。
    ///     进化成某种形态、或者孵出来的就是那个种族，都用这一套。
    ///
    /// 图是静态的，动起来全靠这里的程序动画：浮沉、进食挤压、摸头压扁、升级蹦一下、进化放大、四个闲下来的小动作。
    /// </summary>
    public sealed class SpriteCharacterView : MonoBehaviour, ICharacterView
    {
        public const string Name = "sprite";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterSelf()
        {
            CharacterViews.Register(Name, parent =>
            {
                var go = new GameObject("SpriteCharacter");
                go.transform.SetParent(parent, false);
                return go.AddComponent<SpriteCharacterView>();
            });
        }

        /// <summary>每张图里角色实际占的范围（去掉透明边，UV 0..1）—— 气泡对齐头顶、嘴的位置都按它算。</summary>
        sealed class SpriteInfo
        {
            public UnityEngine.Sprite Sprite;
            public Rect Content;
        }

        static readonly Dictionary<string, SpriteInfo> Cache = new Dictionary<string, SpriteInfo>();

        Transform _body;
        SpriteRenderer _renderer;
        CharacterLook _look = new CharacterLook { Stage = "baby", Form = "", Species = "whale", Scale = 0.53f };
        CharacterState _state = CharacterState.Normal;
        Rect _area = new Rect(0, 0, 240, 240);
        bool _frenzy;
        Vector2? _lookAt;

        // 正在演的一次性动作。
        CharacterAction? _action;
        float _actionTime;
        float _actionLength;
        float _lean;

        public Transform Root => transform;
        public bool Animating => true;

        void Awake()
        {
            _body = new GameObject("Body").transform;
            _body.SetParent(transform, false);
            _renderer = _body.gameObject.AddComponent<SpriteRenderer>();
            ApplySprite();
        }

        static SpriteInfo Load(string path)
        {
            if (Cache.TryGetValue(path, out var info)) return info;
            var sprite = Resources.Load<UnityEngine.Sprite>(path);
            info = new SpriteInfo { Sprite = sprite, Content = new Rect(0, 0, 1, 1) };
            if (sprite != null && sprite.texture.isReadable) info.Content = ContentOf(sprite);
            Cache[path] = info;
            return info;
        }

        /// <summary>扫一遍 alpha，找出不透明部分的外框。</summary>
        static Rect ContentOf(UnityEngine.Sprite sprite)
        {
            var tex = sprite.texture;
            var r = sprite.textureRect;
            var pixels = tex.GetPixels32();
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (var y = (int)r.yMin; y < (int)r.yMax; y += 2)
            {
                for (var x = (int)r.xMin; x < (int)r.xMax; x += 2)
                {
                    if (pixels[y * tex.width + x].a < 40) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < 0) return new Rect(0, 0, 1, 1);
            return Rect.MinMaxRect((minX - r.xMin) / r.width, (minY - r.yMin) / r.height, (maxX - r.xMin) / r.width, (maxY - r.yMin) / r.height);
        }

        static readonly HashSet<string> Animals = new HashSet<string> { "cat", "fox", "bug", "bird" };

        /// <summary>用哪只动物的图：进化形态优先，其次是种族；鲸鱼（或不认识的种族）返回 null。</summary>
        string Animal
        {
            get
            {
                if (!string.IsNullOrEmpty(_look.Form) && Animals.Contains(_look.Form)) return _look.Form;
                if (string.IsNullOrEmpty(_look.Form) && _look.Species != null && Animals.Contains(_look.Species)) return _look.Species;
                return null;
            }
        }

        /// <summary>鲸鱼进化成了没有专属图的形态时，回退到传说档。</summary>
        string WhaleStage => string.IsNullOrEmpty(_look.Form) ? (_look.Stage ?? "baby") : "legend";

        string _dim;

        string StateImage
        {
            get
            {
                if (_action == CharacterAction.Eat) return "eat";
                if (_action == CharacterAction.Pat) return "pat";
                switch (_state)
                {
                    case CharacterState.Hungry: return "hungry";
                    case CharacterState.Sleep: return "sleep";
                    case CharacterState.Sad: return "sad";
                    case CharacterState.Excited: return "excited";
                    default: return _action == CharacterAction.LevelUp || _action == CharacterAction.Evolve || _action == CharacterAction.Morph ? "excited" : "normal";
                }
            }
        }

        string CurrentPath
        {
            get
            {
                var state = StateImage;
                var animal = Animal;
                if (animal == null) return "WhaleSprites/deepseek-" + WhaleStage + "-" + state;
                // 动物没有单独的进食图（进食靠挤压动画）；平常脸可以换成情绪三维的那一张。
                if (state == "eat") state = "normal";
                if (state == "normal" && !_action.HasValue && !string.IsNullOrEmpty(_dim)) return "FormSprites/" + animal + "-normal-" + _dim;
                return "FormSprites/" + animal + "-" + state;
            }
        }

        SpriteInfo Current
        {
            get
            {
                var info = Load(CurrentPath);
                return info.Sprite != null ? info : Load("WhaleSprites/deepseek-baby-normal");
            }
        }

        void ApplySprite()
        {
            var info = Current;
            _renderer.sprite = info.Sprite;
        }

        public void Layout(Rect screenArea)
        {
            _area = screenArea;
        }

        public void SetLook(CharacterLook look)
        {
            _look = look;
            ApplySprite();
        }

        public void SetState(CharacterState state)
        {
            if (_state == state) return;
            _state = state;
            ApplySprite();
        }

        public void SetMoodDim(string dim)
        {
            // 鲸鱼娘立绘没有情绪三维的脸（由表现层的小图标表达）；动物的图有。
            if (_dim == dim) return;
            _dim = dim;
            ApplySprite();
        }

        public void SetFrenzy(bool on)
        {
            _frenzy = on;
        }

        public void Play(CharacterAction action)
        {
            // 进食 / 摸头不打断进化那一下（一辈子就这一次）。
            if ((_action == CharacterAction.Evolve || _action == CharacterAction.Morph) && (action == CharacterAction.Eat || action == CharacterAction.Pat)) return;
            _action = action;
            _actionTime = 0;
            _actionLength = LengthOf(action);
            ApplySprite();
        }

        static float LengthOf(CharacterAction action)
        {
            switch (action)
            {
                case CharacterAction.Eat: return 0.5f;
                case CharacterAction.Pat: return 0.6f;
                case CharacterAction.LevelUp: return 0.6f;
                case CharacterAction.Evolve:
                case CharacterAction.Morph: return 1.6f;
                default: return 1.4f;
            }
        }

        public void LookAt(Vector2? screenPoint)
        {
            _lookAt = screenPoint;
        }

        /// <summary>角色的显示高度（像素）：传说档占满区域高度，其余按体型缩。</summary>
        float DisplayHeight => _area.height * Mathf.Clamp(_look.Scale, 0.3f, 1.2f) * (_frenzy ? 1.08f : 1f);

        void Update()
        {
            var dt = Time.deltaTime;
            var t = Time.time;
            var sprite = _renderer.sprite;
            if (sprite == null) return;

            // 浮沉：周期和幅度跟着状态走。
            float period = 2.6f, amp = 0.022f;
            switch (_state)
            {
                case CharacterState.Sleep: period = 4.2f; amp = 0.012f; break;
                case CharacterState.Excited: period = 1.1f; amp = 0.035f; break;
                case CharacterState.Hungry: period = 3.4f; amp = 0.01f; break;
                case CharacterState.Sad: period = 3.8f; amp = 0.012f; break;
            }
            if (_frenzy) { period = 0.8f; amp = 0.04f; }
            var height = DisplayHeight;
            var bob = Mathf.Sin(t * Mathf.PI * 2 / period) * amp * height;

            var sx = 1f;
            var sy = 1f;
            var rot = 0f;
            var dx = 0f;
            var dy = bob;
            if (_action.HasValue)
            {
                _actionTime += dt;
                var p = Mathf.Clamp01(_actionTime / _actionLength);
                var wave = Mathf.Sin(p * Mathf.PI);
                switch (_action.Value)
                {
                    case CharacterAction.Eat:
                        sx = 1 + 0.08f * wave;
                        sy = 1 - 0.07f * wave;
                        break;
                    case CharacterAction.Pat:
                        sx = 1 + 0.06f * wave;
                        sy = 1 - 0.1f * wave;
                        rot = 5 * Mathf.Sin(p * Mathf.PI * 3) * (1 - p);
                        break;
                    case CharacterAction.LevelUp:
                        dy += 0.12f * height * wave;
                        break;
                    case CharacterAction.Evolve:
                    case CharacterAction.Morph:
                        var pop = 1 + 0.18f * Mathf.Sin(Mathf.Clamp01(p * 1.6f) * Mathf.PI);
                        sx = sy = pop;
                        rot = 8 * Mathf.Sin(p * Mathf.PI * 4) * (1 - p);
                        break;
                    case CharacterAction.Yawn:
                        sy = 1 + 0.06f * wave;
                        sx = 1 - 0.03f * wave;
                        break;
                    case CharacterAction.Wag:
                        rot = 6 * Mathf.Sin(p * Mathf.PI * 6) * wave;
                        break;
                    case CharacterAction.Roll:
                        rot = 360 * Mathf.SmoothStep(0, 1, p);
                        dy += 0.05f * height * wave;
                        break;
                    case CharacterAction.Peek:
                        dx = 0.12f * height * wave;
                        rot = -6 * wave;
                        break;
                }
                if (_actionTime >= _actionLength)
                {
                    _action = null;
                    ApplySprite();
                }
            }

            // 跟着鼠标稍微歪一下头（立绘没有眼睛可转）。
            var leanTarget = 0f;
            if (_lookAt.HasValue)
            {
                var center = _area.center.x;
                leanTarget = Mathf.Clamp((_lookAt.Value.x - center) / (_area.width * 0.5f), -1, 1) * -3f;
            }
            _lean = Mathf.Lerp(_lean, leanTarget, 1 - Mathf.Exp(-dt * 6));

            // 摆位置：图的底边（不透明部分）贴区域底边，水平居中。
            var info = Current;
            var content = info.Content;
            var spriteH = sprite.rect.height;
            var scale = height / Mathf.Max(1, spriteH * content.height);
            var worldScale = scale * sprite.pixelsPerUnit / PetStageCamera.PixelsPerUnit;
            var pivotY = sprite.pivot.y / spriteH;
            var bottomInSprite = (content.yMin - pivotY) * spriteH * scale;
            var centerXInSprite = (content.center.x - sprite.pivot.x / sprite.rect.width) * sprite.rect.width * scale;
            var anchor = new Vector2(_area.center.x - centerXInSprite + dx, _area.yMin - bottomInSprite + dy);

            transform.localPosition = PetStageCamera.ScreenToWorld(anchor);
            _body.localScale = new Vector3(worldScale * sx, worldScale * sy, 1);
            _body.localRotation = Quaternion.Euler(0, 0, rot + _lean);
        }

        public bool HitTest(Vector2 screenPoint)
        {
            var sprite = _renderer.sprite;
            if (sprite == null) return false;
            var local = _body.InverseTransformPoint(PetStageCamera.ScreenToWorld(screenPoint));
            var b = sprite.bounds;
            var u = (local.x - b.min.x) / b.size.x;
            var v = (local.y - b.min.y) / b.size.y;
            if (u < 0 || u > 1 || v < 0 || v > 1) return false;
            var tex = sprite.texture;
            if (!tex.isReadable) return Current.Content.Contains(new Vector2(u, v));
            var r = sprite.textureRect;
            var a = tex.GetPixelBilinear((r.x + u * r.width) / tex.width, (r.y + v * r.height) / tex.height).a;
            return a > 0.15f;
        }

        public Rect ScreenBounds
        {
            get
            {
                var sprite = _renderer.sprite;
                if (sprite == null) return _area;
                var b = sprite.bounds;
                var c = Current.Content;
                var min = _body.TransformPoint(new Vector3(b.min.x + c.xMin * b.size.x, b.min.y + c.yMin * b.size.y, 0));
                var max = _body.TransformPoint(new Vector3(b.min.x + c.xMax * b.size.x, b.min.y + c.yMax * b.size.y, 0));
                var smin = PetStageCamera.WorldToScreen(min);
                var smax = PetStageCamera.WorldToScreen(max);
                return Rect.MinMaxRect(Mathf.Min(smin.x, smax.x), Mathf.Min(smin.y, smax.y), Mathf.Max(smin.x, smax.x), Mathf.Max(smin.y, smax.y));
            }
        }

        /// <summary>立绘没有标注嘴的位置：按头像比例估一个（不透明部分上方约 62% 处）。</summary>
        public Vector2 MouthScreenPoint
        {
            get
            {
                var r = ScreenBounds;
                return new Vector2(r.center.x, r.yMin + r.height * 0.62f);
            }
        }
    }
}
