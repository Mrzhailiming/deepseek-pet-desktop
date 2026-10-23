using System.Collections.Generic;

namespace DeepseekPet.Core
{
    /// <summary>
    /// PetCore 对外发布的快照（JS store 的 state，去掉了网页专属的面板开关 / 拖拽 / 名片 / 头像画法）。
    /// 每次变化都是一份新对象，字段引用没变就是没变 —— 表现层可以按引用比对只重画变了的部分。
    /// 列表字段一律当只读用。
    /// </summary>
    public sealed class PetState
    {
        public PetStats Pet;
        /// <summary>正在飘的特效；每条到 effectTtlMs 自己撤掉。</summary>
        public List<PetEffect> Effects = new List<PetEffect>();
        public int ComboCount;
        public double ComboMultiplier = 1;
        /// <summary>normal / gold / epic。</summary>
        public string ComboTier = "normal";
        /// <summary>每吃一口 +1：表现层看到它变了就播一次进食动画。</summary>
        public int EatKey;
        /// <summary>每摸一次 +1。</summary>
        public int PatKey;
        /// <summary>"eat" / "pat" / null，500ms 后自己清掉。</summary>
        public string LastAct;
        /// <summary>闲下来的小动作（IDLE_ACTS 之一）或 null。</summary>
        public string IdleAct;
        public long TotalFeeds;
        public long TotalTokens;
        public TokensBySource TokensBySource;
        public int Snacks;
        public List<string> Achievements;
        public DailyProgress Daily;
        public long StreakDay;
        public long StreakCount;
        public long Pats;
        public SkillTable Skills;
        public PetMemory Memory;
        public CardPos Pos;
        public long LastFeedAt;
        public bool Asleep;
        public PetBubble Bubble;
        public PetBuff Buff;
        public string ActivePetId;
        public int PetCount;
        public List<string> Favorites;
        public List<Egg> Eggs;
        public List<string> PetOrder;
        public Egg HatchingEgg;
        public MiniGame MiniGame;
        /// <summary>种族天赋的说明（「饱食+10%」）。</summary>
        public string Talent;
        public int Prestige;
        public List<string> FormsOwned;

        internal PetState Copy()
        {
            return (PetState)MemberwiseClone();
        }
    }

    /// <summary>patch 里的一项：「设了没有」和值分开记，因为 null 本身也是合法的值（buff: null）。</summary>
    internal struct Opt<T>
    {
        public bool Has;
        public T Value;

        public void Set(T value)
        {
            Has = true;
            Value = value;
        }

        public T Or(T fallback)
        {
            return Has ? Value : fallback;
        }
    }

    /// <summary>JS 里那份「正在攒的 patch」：一次操作里各处往里填，最后 commit 一次。</summary>
    internal sealed class Patch
    {
        public Opt<PetStats> Pet;
        public Opt<List<PetEffect>> Effects;
        public Opt<int> ComboCount;
        public Opt<double> ComboMultiplier;
        public Opt<string> ComboTier;
        public Opt<int> EatKey;
        public Opt<int> PatKey;
        public Opt<string> LastAct;
        public Opt<string> IdleAct;
        public Opt<long> TotalFeeds;
        public Opt<long> TotalTokens;
        public Opt<TokensBySource> TokensBySource;
        public Opt<int> Snacks;
        public Opt<List<string>> Achievements;
        public Opt<DailyProgress> Daily;
        public Opt<long> StreakDay;
        public Opt<long> StreakCount;
        public Opt<long> Pats;
        public Opt<SkillTable> Skills;
        public Opt<PetMemory> Memory;
        public Opt<long> LastFeedAt;
        public Opt<bool> Asleep;
        public Opt<PetBubble> Bubble;
        public Opt<PetBuff> Buff;
        public Opt<List<string>> Favorites;
        public Opt<List<string>> PetOrder;
        public Opt<List<Egg>> Eggs;
        public Opt<Egg> HatchingEgg;
        public Opt<MiniGame> MiniGame;
        public Opt<int> Prestige;
        public Opt<List<string>> FormsOwned;

        /// <summary>JS 的 Object.keys(patch).length > 0。</summary>
        public bool Any =>
            Pet.Has || Effects.Has || ComboCount.Has || ComboMultiplier.Has || ComboTier.Has || EatKey.Has || PatKey.Has
            || LastAct.Has || IdleAct.Has || TotalFeeds.Has || TotalTokens.Has || TokensBySource.Has || Snacks.Has
            || Achievements.Has || Daily.Has || StreakDay.Has || StreakCount.Has || Pats.Has || Skills.Has || Memory.Has
            || LastFeedAt.Has || Asleep.Has || Bubble.Has || Buff.Has || Favorites.Has || PetOrder.Has || Eggs.Has
            || HatchingEgg.Has || MiniGame.Has || Prestige.Has || FormsOwned.Has;

        public PetState ApplyTo(PetState state)
        {
            var next = state.Copy();
            if (Pet.Has) next.Pet = Pet.Value;
            if (Effects.Has) next.Effects = Effects.Value;
            if (ComboCount.Has) next.ComboCount = ComboCount.Value;
            if (ComboMultiplier.Has) next.ComboMultiplier = ComboMultiplier.Value;
            if (ComboTier.Has) next.ComboTier = ComboTier.Value;
            if (EatKey.Has) next.EatKey = EatKey.Value;
            if (PatKey.Has) next.PatKey = PatKey.Value;
            if (LastAct.Has) next.LastAct = LastAct.Value;
            if (IdleAct.Has) next.IdleAct = IdleAct.Value;
            if (TotalFeeds.Has) next.TotalFeeds = TotalFeeds.Value;
            if (TotalTokens.Has) next.TotalTokens = TotalTokens.Value;
            if (TokensBySource.Has) next.TokensBySource = TokensBySource.Value;
            if (Snacks.Has) next.Snacks = Snacks.Value;
            if (Achievements.Has) next.Achievements = Achievements.Value;
            if (Daily.Has) next.Daily = Daily.Value;
            if (StreakDay.Has) next.StreakDay = StreakDay.Value;
            if (StreakCount.Has) next.StreakCount = StreakCount.Value;
            if (Pats.Has) next.Pats = Pats.Value;
            if (Skills.Has) next.Skills = Skills.Value;
            if (Memory.Has) next.Memory = Memory.Value;
            if (LastFeedAt.Has) next.LastFeedAt = LastFeedAt.Value;
            if (Asleep.Has) next.Asleep = Asleep.Value;
            if (Bubble.Has) next.Bubble = Bubble.Value;
            if (Buff.Has) next.Buff = Buff.Value;
            if (Favorites.Has) next.Favorites = Favorites.Value;
            if (PetOrder.Has) next.PetOrder = PetOrder.Value;
            if (Eggs.Has) next.Eggs = Eggs.Value;
            if (HatchingEgg.Has) next.HatchingEgg = HatchingEgg.Value;
            if (MiniGame.Has) next.MiniGame = MiniGame.Value;
            if (Prestige.Has) next.Prestige = Prestige.Value;
            if (FormsOwned.Has) next.FormsOwned = FormsOwned.Value;
            return next;
        }
    }
}
