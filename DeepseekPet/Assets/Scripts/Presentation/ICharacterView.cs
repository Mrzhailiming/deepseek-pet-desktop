using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeepseekPet.Presentation
{
    /// <summary>持续的状态（同一时刻只有一个；优先级由 PetPresenter 决定：饿 &gt; 睡 &gt; 难过 &gt; 兴奋 &gt; 普通）。</summary>
    public enum CharacterState
    {
        Normal,
        Hungry,
        Sleep,
        Sad,
        Excited,
    }

    /// <summary>一次性的动作：演完自己回到持续状态。</summary>
    public enum CharacterAction
    {
        Eat,
        Pat,
        LevelUp,
        Evolve,
        Morph,
        Yawn,
        Wag,
        Roll,
        Peek,
    }

    /// <summary>长相：鲸鱼的等级档，或进化形态；外加种族（多宠物）。</summary>
    public struct CharacterLook
    {
        /// <summary>WHALE_STAGES 的 key：baby / young / adult / legend。</summary>
        public string Stage;
        /// <summary>进化形态（FORMS 的 key）；"" = 没分化。</summary>
        public string Form;
        /// <summary>种族（PET_SPECIES 的 key）：whale / cat / fox / bird / bug。</summary>
        public string Species;
        /// <summary>相对体型：传说档 = 1，幼崽 ≈ 0.53（WHALE_STAGES 的 size / 64）。</summary>
        public float Scale;

        public bool Same(CharacterLook other)
        {
            return Stage == other.Stage && Form == other.Form && Species == other.Species && Mathf.Approximately(Scale, other.Scale);
        }
    }

    /// <summary>
    /// 角色怎么画。表现层（PetPresenter）、特效、窗口穿透都只认这个接口，不碰具体渲染。
    ///
    /// 首个实现是立绘（Pet.Character.Sprite 的 SpriteCharacterView）；以后加 Live2D / Spine / 3D 模型，
    /// 只是多一个实现（放在各自的 asmdef 里，没装对应 SDK 时整个排除），在 <see cref="CharacterViews"/> 里登记一个名字，
    /// 换角色靠配置选名字 —— Core、Bridge、特效都不用动。
    ///
    /// 坐标一律是 Unity 的屏幕像素（窗口客户区，原点左下，y 向上）。实现自己挂在 <see cref="Root"/> 下，
    /// 用世界空间渲染（正交相机已经由 PetPresenter 摆好：1 世界单位 = <see cref="PetStageCamera.PixelsPerUnit"/> 像素）。
    /// </summary>
    public interface ICharacterView
    {
        Transform Root { get; }

        /// <summary>把角色摆进这个屏幕矩形（底边对齐、水平居中，按 look 的体型缩放）。</summary>
        void Layout(Rect screenArea);

        void SetLook(CharacterLook look);
        void SetState(CharacterState state);

        /// <summary>写在脸上的那一维情绪（"pride" / "curiosity" / "concern"），没有则 null。不支持就忽略。</summary>
        void SetMoodDim(string dim);

        /// <summary>暴食：画得更大、更躁动。</summary>
        void SetFrenzy(bool on);

        void Play(CharacterAction action);

        /// <summary>看向这个屏幕点（null = 回正）。不支持就忽略。</summary>
        void LookAt(Vector2? screenPoint);

        /// <summary>这个屏幕点是不是点在角色身上（立绘按 alpha，Live2D 按 drawable，3D 按 collider）。窗口穿透靠它。</summary>
        bool HitTest(Vector2 screenPoint);

        /// <summary>角色此刻在屏幕上占的矩形（气泡、特效往这儿对齐）。</summary>
        Rect ScreenBounds { get; }

        /// <summary>嘴在哪儿（食物飞向这里）。</summary>
        Vector2 MouthScreenPoint { get; }

        /// <summary>有没有动画在演（没有的话可以降帧）。</summary>
        bool Animating { get; }
    }

    /// <summary>角色实现的登记处：各实现的程序集在启动时（BeforeSceneLoad）登记一个名字和工厂。</summary>
    public static class CharacterViews
    {
        static readonly Dictionary<string, Func<Transform, ICharacterView>> Factories = new Dictionary<string, Func<Transform, ICharacterView>>();

        public static void Register(string name, Func<Transform, ICharacterView> factory)
        {
            Factories[name] = factory;
        }

        public static IEnumerable<string> Names => Factories.Keys;

        /// <summary>按名字建一个；名字不认识就用第一个登记的（总得有个角色）。</summary>
        public static ICharacterView Create(string name, Transform parent)
        {
            if (name != null && Factories.TryGetValue(name, out var factory)) return factory(parent);
            foreach (var any in Factories.Values) return any(parent);
            throw new InvalidOperationException("没有任何角色实现登记（Pet.Character.* 程序集没编进来？）");
        }
    }

    /// <summary>世界坐标和屏幕像素的换算（正交相机，原点在窗口左下角）。</summary>
    public static class PetStageCamera
    {
        public const float PixelsPerUnit = 100f;

        public static Vector3 ScreenToWorld(Vector2 screen, float z = 0)
        {
            return new Vector3(screen.x / PixelsPerUnit, screen.y / PixelsPerUnit, z);
        }

        public static Vector2 WorldToScreen(Vector3 world)
        {
            return new Vector2(world.x * PixelsPerUnit, world.y * PixelsPerUnit);
        }
    }
}
