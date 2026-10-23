using System;
using System.Collections.Generic;

namespace DeepseekPet.Core
{
    // 原 JS 的数据形状（types.d.ts）。都是可变类，但 PetCore 按 JS 的习惯把它们当不可变值用：
    // 要改就先 Clone 一份再改，靠「引用变没变」判断有没有变化（settleVitals / tick 都这么判）。

    /// <summary>宠物本体的数值（JS 的 pet）。name / species / avatar / icon 跟着宠物档案走，其余是进度。</summary>
    public sealed class PetStats
    {
        public string Name = "";
        public string Species = "";
        public string Avatar = "";
        public string Icon = "";
        public int Mood = 80;
        public int Hunger = 60;
        public int Energy = 75;
        public int Curiosity;
        public int Pride;
        public int Concern;
        public int Exp;
        public int Level = 1;
        /// <summary>进化形态（FORMS 的 key）；"" = 还没分化。</summary>
        public string Form = "";

        public PetStats Clone()
        {
            return (PetStats)MemberwiseClone();
        }

        /// <summary>情绪三维按 key 取值（MOOD_DIMS 的 key：curiosity / pride / concern）。</summary>
        public int Dim(string key)
        {
            switch (key)
            {
                case "curiosity": return Curiosity;
                case "pride": return Pride;
                case "concern": return Concern;
                default: throw new ArgumentException("没有这一维：" + key);
            }
        }

        public void SetDim(string key, int value)
        {
            switch (key)
            {
                case "curiosity": Curiosity = value; break;
                case "pride": Pride = value; break;
                case "concern": Concern = value; break;
                default: throw new ArgumentException("没有这一维：" + key);
            }
        }
    }

    public sealed class SkillRecord
    {
        public int Xp;
        public int Level = 1;
        public long Mastery;

        public SkillRecord Clone()
        {
            return (SkillRecord)MemberwiseClone();
        }
    }

    /// <summary>技能表：key → 记录。遍历顺序以 PetData.Skills 为准，不靠字典顺序。</summary>
    public sealed class SkillTable
    {
        public readonly Dictionary<string, SkillRecord> Rows = new Dictionary<string, SkillRecord>(StringComparer.Ordinal);

        public SkillRecord this[string key] => Rows.TryGetValue(key, out var row) ? row : null;

        /// <summary>浅拷贝（行对象共用，改哪行就换哪行）。</summary>
        public SkillTable Clone()
        {
            var next = new SkillTable();
            foreach (var pair in Rows) next.Rows[pair.Key] = pair.Value;
            return next;
        }
    }

    public sealed class MemoryRow
    {
        public string Name;
        public long Count;

        public MemoryRow(string name, long count)
        {
            Name = name;
            Count = count;
        }
    }

    /// <summary>宠物记忆：常改的文件、常用的工具、常在几点干活。</summary>
    public sealed class PetMemory
    {
        public List<MemoryRow> Files = new List<MemoryRow>();
        public List<MemoryRow> Tools = new List<MemoryRow>();
        public long[] Hours = new long[24];
        public long BornDay;
        public long Errors;
        public long Recoveries;

        /// <summary>JS 的 Object.assign({}, memory, { hours: hours.slice() })：行数组共用，hours 另拷一份。</summary>
        public PetMemory Clone()
        {
            var next = (PetMemory)MemberwiseClone();
            next.Hours = (long[])Hours.Clone();
            return next;
        }
    }

    /// <summary>当天的任务进度。day 对不上今天就整份重来。</summary>
    public sealed class DailyProgress
    {
        public long Day;
        public long Feeds;
        public long Tools;
        public long BestCombo;
        public long Pats;
        public long Tokens;
        public long Snacks;
        public List<string> Done = new List<string>();

        public static DailyProgress Empty(long day)
        {
            return new DailyProgress { Day = day };
        }
    }

    /// <summary>三个喂食源各吃了多少 token。</summary>
    public sealed class TokensBySource
    {
        public long UserInput;
        public long Generation;
        public long ToolResult;

        public long this[string source]
        {
            get
            {
                switch (source)
                {
                    case FeedSource.UserInput: return UserInput;
                    case FeedSource.Generation: return Generation;
                    case FeedSource.ToolResult: return ToolResult;
                    default: throw new ArgumentException("没有这个喂食源：" + source);
                }
            }
            set
            {
                switch (source)
                {
                    case FeedSource.UserInput: UserInput = value; break;
                    case FeedSource.Generation: Generation = value; break;
                    case FeedSource.ToolResult: ToolResult = value; break;
                    default: throw new ArgumentException("没有这个喂食源：" + source);
                }
            }
        }

        public TokensBySource Clone()
        {
            return (TokensBySource)MemberwiseClone();
        }
    }

    /// <summary>三个喂食源（BASE_EXP / FOOD_ICON 的 key）。</summary>
    public static class FeedSource
    {
        public const string UserInput = "user_input";
        public const string Generation = "generation";
        public const string ToolResult = "tool_result";
    }

    /// <summary>网页卡片的拖拽偏移。桌宠用不上，只为存档原样往返。</summary>
    public sealed class CardPos
    {
        public long Dx;
        public long Dy;
    }

    public sealed class Egg
    {
        public string Id;
        public string Type;
        public long ObtainedAt;
    }

    public sealed class GlobalStats
    {
        public long TotalTokensAllTime;
        public long TotalFeedsAllTime;
        public long AchievementsUnlockedAllTime;
        public long PetsHatched = 1;
        public List<string> EggsObtained = new List<string>();
    }

    /// <summary>一只宠物的完整档案（多宠物集合里的一项，也是存档里 pets 的一项）。</summary>
    public sealed class PetRecord
    {
        public string Id;
        public string Species;
        public string Name;
        public string Avatar;
        public string Icon;
        public long BornAt;
        public PetStats Pet;
        public long TotalFeeds;
        public long TotalTokens;
        public TokensBySource TokensBySource;
        public int Snacks;
        public List<string> Achievements;
        public DailyProgress Daily;
        public long StreakDay;
        public long StreakCount;
        public long Pats;
        public CardPos Pos;
        public long LastFeedAt;
        public SkillTable Skills;
        public PetMemory Memory;
        public int Prestige;
        public List<string> FormsOwned;
    }

    /// <summary>
    /// 多宠物集合：id → 档案，保持插入顺序（JS 对象的键序：换宠物顺序、兜底「第一只」都靠它）。
    /// </summary>
    public sealed class PetCollection
    {
        readonly List<string> _order = new List<string>();
        readonly Dictionary<string, PetRecord> _byId = new Dictionary<string, PetRecord>(StringComparer.Ordinal);

        public int Count => _order.Count;
        public IReadOnlyList<string> Ids => _order;

        public PetRecord this[string id]
        {
            get => _byId.TryGetValue(id, out var rec) ? rec : null;
            set
            {
                if (!_byId.ContainsKey(id)) _order.Add(id);
                _byId[id] = value;
            }
        }

        public bool Contains(string id)
        {
            return id != null && _byId.ContainsKey(id);
        }
    }

    /// <summary>暴食 BUFF（瞬时态，不落盘）。</summary>
    public sealed class PetBuff
    {
        public string Kind;
        public long Until;
    }

    /// <summary>头顶的台词气泡。</summary>
    public sealed class PetBubble
    {
        public string Key;
        /// <summary>场合（BUBBLE_LINES 的 key，或 advice / chat / comeback 这类直接给文案的场合）。</summary>
        public string Kind;
        public string Text;
    }

    /// <summary>
    /// 一条飘字特效：喂食那一口（Text 为 null，按 FoodAmount / ExpAmount 拼字），
    /// 或一条通知（进阶 / 成就 / 任务 / 摸头 …，Text 是整句）。Source 标明是哪一类。
    /// </summary>
    public sealed class PetEffect
    {
        public string Key;
        public string Icon;
        public string Text;
        public int FoodAmount;
        public int ExpAmount;
        public long Tokens;
        public string FoodTier;
        public string Source;
        public int ComboCount;
        public double ComboMultiplier = 1;
        public string Tier;
    }

    public sealed class MiniGameTarget
    {
        public int Id;
        public string Icon;
        public long X;
        public long Y;
        public bool Caught;

        public MiniGameTarget Clone()
        {
            return (MiniGameTarget)MemberwiseClone();
        }
    }

    public sealed class MiniGame
    {
        public List<MiniGameTarget> Targets = new List<MiniGameTarget>();
        public int Current;
        public long StartedAt;
        public long Deadline;
        public int Score;
        public bool Finished;
        public bool Success;

        public MiniGame Clone()
        {
            var next = (MiniGame)MemberwiseClone();
            next.Targets = new List<MiniGameTarget>(Targets);
            return next;
        }
    }
}
