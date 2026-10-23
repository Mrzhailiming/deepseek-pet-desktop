using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace DeepseekPet.Core
{
    public sealed class SkillDef
    {
        public string Key;
        public string Icon;
        public string Label;
    }

    /// <summary>等级形态（WHALE_STAGES）。美术参数原样保留，表现层要用。</summary>
    public sealed class StageDef
    {
        public string Key;
        public string Label;
        public int MinLevel;
        public int Size;
        public JObject Raw;
    }

    public sealed class FormDef
    {
        public string Key;
        public string Skill;
        public string Label;
        public string Icon;
        public int Size;
    }

    public sealed class MoodDimDef
    {
        public string Key;
        public string Icon;
        public string Label;
        /// <summary>越线那一刻说的台词池。</summary>
        public string Line;
    }

    /// <summary>成就。判定写在 PetTables 里，按 id 绑上来。</summary>
    public sealed class AchievementDef
    {
        public string Id;
        public string Icon;
        public string Label;
        public string Hint;
        internal Func<AchievementStats, PetConfig, bool> Test;
    }

    public sealed class QuestDef
    {
        public string Id;
        public string Icon;
        public string Label;
        public long Goal;
        internal Func<DailyProgress, long> Of;

        /// <summary>这一份当天进度里，这个任务走到了多少（和 Goal 比）。</summary>
        public long Progress(DailyProgress daily) => Of(daily);
    }

    public sealed class EggTypeDef
    {
        public string Key;
        public string Label;
        public string Icon;
        public string Shell;
        /// <summary>孵出来是哪个种族；"random" = 随便一个。</summary>
        public string Species;
    }

    public sealed class SpeciesDef
    {
        public string Key;
        public string Name;
        public string Icon;
        public string Avatar;
        public string Label;
        public string Talent;
        public double HungerBonus;
        /// <summary>技能 key → 加成（0.15 = +15%）；没有则空表。</summary>
        public Dictionary<string, double> SkillBonus = new Dictionary<string, double>();
    }

    public sealed class EggMilestoneDef
    {
        public string Id;
        public string Egg;
        internal Func<GlobalStats, bool> Test;
    }

    public sealed class MilestoneLevelDef
    {
        public int Level;
        public string Label;
        public string Icon;
    }

    /// <summary>
    /// 数据表：PetTools export-data 从原 JS（config.js / constants.js）导出的 pet-data.json，别手改。
    /// 带判定函数的表（成就 / 任务 / 蛋里程碑 / 称号）只导出数据，判定在 PetTables 里按 id 补上；
    /// 对不上（JS 加了一条 C# 没写）就在 Parse 时报错。
    /// </summary>
    public sealed class PetData
    {
        public JObject Defaults;

        public int StateVersion;
        public int MaxPets;
        public int MaxEggs;
        public int ExpPerLevel;
        public int MemoryToolTop;
        public int MemorySlack;
        public int CallNameCap;
        public int DailyQuestCount;
        public string SkillFallback;

        public string SnackIcon;
        public string EvolveIcon;
        public string PatIcon;
        public string QuestIcon;
        public string BadgeIcon;

        public Dictionary<string, int> BaseExp;
        /// <summary>喂食源 → 食物档位 → 图标。</summary>
        public Dictionary<string, Dictionary<string, string>> FoodIcon;
        public Dictionary<string, string[]> BubbleLines;

        public List<SkillDef> Skills;
        public Dictionary<string, SkillDef> SkillById;
        public Dictionary<string, string> ToolSkill;
        public HashSet<string> EditTools;

        public List<StageDef> WhaleStages;
        public List<FormDef> Forms;
        public Dictionary<string, FormDef> FormByKey;
        public Dictionary<string, FormDef> FormBySkill;

        public List<MoodDimDef> MoodDims;
        public HashSet<string> DimLineKinds;
        public HashSet<string> CareKinds;
        public string[] IdleActs;

        public List<AchievementDef> Achievements;
        public Dictionary<string, AchievementDef> AchievementById;
        public List<QuestDef> QuestPool;
        public Dictionary<string, QuestDef> QuestById;
        public List<EggTypeDef> EggTypes;
        public Dictionary<string, EggTypeDef> EggTypeByKey;
        /// <summary>种族表，保持 JS 里的键序（彩虹蛋随机种族按这个顺序取下标）。</summary>
        public List<SpeciesDef> Species;
        public Dictionary<string, SpeciesDef> SpeciesByKey;
        public List<EggMilestoneDef> EggMilestones;
        public List<MilestoneLevelDef> MilestoneLevels;
        public List<string> Titles;

        public Dictionary<string, int> DynamicLineCounts;
        public List<string> AdviceLines;
        public List<string> ChatLines;

        public static PetData Parse(string json)
        {
            var root = JObject.Parse(json);
            var c = (JObject)root["constants"];
            var icons = (JObject)root["icons"];
            var data = new PetData
            {
                Defaults = (JObject)root["defaults"],
                StateVersion = c.Value<int>("stateVersion"),
                MaxPets = c.Value<int>("maxPets"),
                MaxEggs = c.Value<int>("maxEggs"),
                ExpPerLevel = c.Value<int>("expPerLevel"),
                MemoryToolTop = c.Value<int>("memoryToolTop"),
                MemorySlack = c.Value<int>("memorySlack"),
                CallNameCap = c.Value<int>("callNameCap"),
                DailyQuestCount = c.Value<int>("dailyQuestCount"),
                SkillFallback = c.Value<string>("skillFallback"),
                SnackIcon = icons.Value<string>("snack"),
                EvolveIcon = icons.Value<string>("evolve"),
                PatIcon = icons.Value<string>("pat"),
                QuestIcon = icons.Value<string>("quest"),
                BadgeIcon = icons.Value<string>("badge"),
                BaseExp = root["baseExp"].ToObject<Dictionary<string, int>>(),
                FoodIcon = root["foodIcon"].ToObject<Dictionary<string, Dictionary<string, string>>>(),
                BubbleLines = root["bubbleLines"].ToObject<Dictionary<string, string[]>>(),
                ToolSkill = root["toolSkill"].ToObject<Dictionary<string, string>>(),
                EditTools = new HashSet<string>(root["editTools"].ToObject<string[]>()),
                DimLineKinds = new HashSet<string>(root["dimLineKinds"].ToObject<string[]>()),
                CareKinds = new HashSet<string>(root["careKinds"].ToObject<string[]>()),
                IdleActs = root["idleActs"].ToObject<string[]>(),
                Titles = root["titles"].ToObject<List<string>>(),
                DynamicLineCounts = root["dynamicLineCounts"].ToObject<Dictionary<string, int>>(),
                AdviceLines = root["adviceLines"].ToObject<List<string>>(),
                ChatLines = root["chatLines"].ToObject<List<string>>(),
            };

            data.Skills = root["skills"].Select(t => new SkillDef { Key = t.Value<string>("key"), Icon = t.Value<string>("icon"), Label = t.Value<string>("label") }).ToList();
            data.SkillById = data.Skills.ToDictionary(s => s.Key);
            data.WhaleStages = root["whaleStages"].Select(t => new StageDef
            {
                Key = t.Value<string>("key"), Label = t.Value<string>("label"), MinLevel = t.Value<int>("minLevel"), Size = t.Value<int>("size"), Raw = (JObject)t,
            }).ToList();
            data.Forms = root["forms"].Select(t => new FormDef
            {
                Key = t.Value<string>("key"), Skill = t.Value<string>("skill"), Label = t.Value<string>("label"), Icon = t.Value<string>("icon"), Size = t.Value<int>("size"),
            }).ToList();
            data.FormByKey = data.Forms.ToDictionary(f => f.Key);
            data.FormBySkill = data.Forms.ToDictionary(f => f.Skill);
            data.MoodDims = root["moodDims"].Select(t => new MoodDimDef
            {
                Key = t.Value<string>("key"), Icon = t.Value<string>("icon"), Label = t.Value<string>("label"), Line = t.Value<string>("line"),
            }).ToList();
            data.Achievements = root["achievements"].Select(t => new AchievementDef
            {
                Id = t.Value<string>("id"), Icon = t.Value<string>("icon"), Label = t.Value<string>("label"), Hint = t.Value<string>("hint"),
            }).ToList();
            data.AchievementById = data.Achievements.ToDictionary(a => a.Id);
            data.QuestPool = root["questPool"].Select(t => new QuestDef
            {
                Id = t.Value<string>("id"), Icon = t.Value<string>("icon"), Label = t.Value<string>("label"), Goal = t.Value<long>("goal"),
            }).ToList();
            data.QuestById = data.QuestPool.ToDictionary(q => q.Id);
            data.EggTypes = root["eggTypes"].Select(t => new EggTypeDef
            {
                Key = t.Value<string>("key"), Label = t.Value<string>("label"), Icon = t.Value<string>("icon"), Shell = t.Value<string>("shell"), Species = t.Value<string>("species"),
            }).ToList();
            data.EggTypeByKey = data.EggTypes.ToDictionary(e => e.Key);
            data.Species = new List<SpeciesDef>();
            foreach (var pair in (JObject)root["petSpecies"])
            {
                var t = (JObject)pair.Value;
                var def = new SpeciesDef
                {
                    Key = pair.Key, Name = t.Value<string>("name"), Icon = t.Value<string>("icon"), Avatar = t.Value<string>("avatar"),
                    Label = t.Value<string>("label"), Talent = t.Value<string>("talent"), HungerBonus = t.Value<double?>("hungerBonus") ?? 0,
                };
                if (t["skillBonus"] is JObject bonus)
                {
                    foreach (var b in bonus) def.SkillBonus[b.Key] = b.Value.Value<double>();
                }
                data.Species.Add(def);
            }
            data.SpeciesByKey = data.Species.ToDictionary(s => s.Key);
            data.EggMilestones = root["eggMilestones"].Select(t => new EggMilestoneDef { Id = t.Value<string>("id"), Egg = t.Value<string>("egg") }).ToList();
            data.MilestoneLevels = root["milestoneLevels"].Select(t => new MilestoneLevelDef
            {
                Level = t.Value<int>("level"), Label = t.Value<string>("label"), Icon = t.Value<string>("icon"),
            }).ToList();

            PetTables.Bind(data);
            return data;
        }
    }
}
