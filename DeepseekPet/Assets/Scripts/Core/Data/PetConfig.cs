using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace DeepseekPet.Core
{
    /// <summary>
    /// 生效配置。字段与原 JS 的 DEFAULTS 一一对应（PascalCase ↔ camelCase），**默认值不写在这里**：
    /// 全部来自 pet-data.json 的 defaults（PetTools export-data 从 config.js 导出）。
    ///
    /// 类型按默认值定：开关 bool、文案 string、*Ms 是 long、份量 / 次数 / 阈值是 int、速率 / 倍数 / 概率是 double。
    /// 覆盖规则照抄 resolveConfig：只收同类型的值；int / long 字段另外要求是整数（JS 里能写 2.5，这里不收）。
    /// </summary>
    public sealed class PetConfig
    {
        public bool Enabled;
        public long ComboWindowMs;
        public int MaxCombo;
        public int MinFood;
        public int MaxFood;
        public double TokensPerFood;
        public double FoodPerDouble;
        public double HungerRegenPerMin;
        public bool ManualFeedEnabled;
        public int ManualFeedFood;
        public int ManualFeedExp;
        public int ManualSnackMax;
        public long ManualSnackRegenMs;
        public int HungryAt;
        public bool VitalsEnabled;
        public int MoodPerFeed;
        public int MoodPerPat;
        public double MoodDecayPerMin;
        public double MoodDropPerMinHungry;
        public double MoodCrashPerMinStarving;
        public int MoodExpFreezeAt;
        public double MoodExpSwing;
        public int MoodSadAt;
        public int EnergyPerFeed;
        public double EnergyRegenPerMin;
        public double EnergySleepFactor;
        public int LowEnergyAt;
        public double TiredExpFactor;
        public bool SleepEnabled;
        public long SleepAfterMs;
        public double SleepHungerFactor;
        public bool PatEnabled;
        public long PatCooldownMs;
        public bool MiniGameEnabled;
        public long MiniGameCooldownMs;
        public int MiniGameTargets;
        public long MiniGameTimeoutMs;
        public int MiniGameExpReward;
        public int MiniGameSnackReward;
        public int MiniGameMoodReward;
        public bool BubbleEnabled;
        public long BubbleTtlMs;
        public long BubbleMinGapMs;
        public int DynamicLinesEvery;
        public bool DragEnabled;
        public bool AchievementsEnabled;
        public int AchievementSnacks;
        public bool DailyEnabled;
        public int DailyQuestExp;
        public int DailyQuestSnacks;
        public bool PickyEnabled;
        public string FavoriteSource;
        public double FavoriteBonus;
        public int BoredomAfter;
        public double BoredomFactor;
        public double VarietyBonus;
        public bool FrenzyEnabled;
        public long FrenzyMs;
        public double FrenzyExpFactor;
        public double FrenzyFoodFactor;
        public bool EggDropEnabled;
        public double EggDropBaseChance;
        public double EggDropComboBonus;
        public bool SkillsEnabled;
        public int SkillXpPerLevel;
        public int SkillMaxLevel;
        public int MasteryXpPerTier;
        public bool EvolveEnabled;
        public int EvolveMinLevel;
        public int EvolveMinSkillLevel;
        public bool PrestigeEnabled;
        public int PrestigeMinLevel;
        public double PrestigeExpBonus;
        public bool MemoryEnabled;
        public int MemoryFileTop;
        public bool AdviceEnabled;
        public int AdviceMinSkillLevel;
        public long AdviceCooldownMs;
        public int AdviceRepeatEditAt;
        public int AdviceErrorStreakAt;
        public bool CareEnabled;
        public long CareCooldownMs;
        public int CareNightFrom;
        public int CareNightTo;
        public long CareMarathonMs;
        public long CareComebackMs;
        public bool MoodDimsEnabled;
        public int CuriosityPerNew;
        public int CuriosityPerResearch;
        public int PridePerWin;
        public int PridePerLevelup;
        public int ConcernPerError;
        public int ConcernPerWorry;
        public double MoodDimDecayPerMin;
        public int MoodDimAt;
        public bool EyeTrackEnabled;
        public double EyeTrackMax;
        public long EyeTrackIdleMs;
        public bool IdleActEnabled;
        public long IdleActEveryMs;
        public long IdleActQuietMs;
        public long IdleActMs;
        public string AvatarStyle;
        public bool EffectsEnabled;
        public long EffectTtlMs;
        public bool FlyFromConversation;
        public long FreshnessMs;
        public bool Persist;
        public long SaveDebounceMs;
        public long OfflineRegenCapMs;
        public bool OfflineSelfCare;
        public int OfflineHungerCap;
        public int OfflineMoodFloor;
        public string PetName;
        public string PetSpecies;
        public string PetAvatar;
        public string PetIcon;

        static readonly Dictionary<string, FieldInfo> Fields = BuildFieldMap();

        static Dictionary<string, FieldInfo> BuildFieldMap()
        {
            var map = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);
            foreach (var field in typeof(PetConfig).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                map[char.ToLowerInvariant(field.Name[0]) + field.Name.Substring(1)] = field;
            }
            return map;
        }

        /// <summary>默认值 + 覆盖（overrides 可为 null；是 JSON 对象，同 localStorage 里的 dsh-pet-plugin/config）。</summary>
        public static PetConfig Create(PetData data, JObject overrides = null)
        {
            var config = new PetConfig();
            var seen = new HashSet<string>();
            foreach (var pair in data.Defaults)
            {
                if (!Fields.TryGetValue(pair.Key, out var field))
                {
                    throw new InvalidOperationException("pet-data.json 的 defaults 多了 " + pair.Key + "，PetConfig 里没有对应字段");
                }
                if (!Assign(config, field, pair.Value)) throw new InvalidOperationException("defaults." + pair.Key + " 的类型和 PetConfig 对不上");
                seen.Add(pair.Key);
            }
            foreach (var key in Fields.Keys)
            {
                if (!seen.Contains(key)) throw new InvalidOperationException("PetConfig." + key + " 在 pet-data.json 的 defaults 里没有");
            }
            if (overrides != null)
            {
                foreach (var pair in overrides)
                {
                    if (Fields.TryGetValue(pair.Key, out var field)) Assign(config, field, pair.Value);
                }
            }
            return config;
        }

        static bool Assign(PetConfig config, FieldInfo field, JToken value)
        {
            if (value == null) return false;
            var type = field.FieldType;
            if (type == typeof(bool))
            {
                if (value.Type != JTokenType.Boolean) return false;
                field.SetValue(config, value.Value<bool>());
                return true;
            }
            if (type == typeof(string))
            {
                if (value.Type != JTokenType.String) return false;
                field.SetValue(config, value.Value<string>());
                return true;
            }
            if (value.Type != JTokenType.Integer && value.Type != JTokenType.Float) return false;
            var number = value.Value<double>();
            if (double.IsNaN(number) || double.IsInfinity(number)) return false;
            if (type == typeof(double))
            {
                field.SetValue(config, number);
                return true;
            }
            if (Math.Floor(number) != number) return false;
            if (type == typeof(int))
            {
                if (number < int.MinValue || number > int.MaxValue) return false;
                field.SetValue(config, (int)number);
                return true;
            }
            if (type == typeof(long))
            {
                field.SetValue(config, (long)number);
                return true;
            }
            return false;
        }
    }
}
