using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace DeepseekPet.Core
{
    /// <summary>gainSkill 的结果。</summary>
    public sealed class SkillGain
    {
        public SkillTable Skills;
        /// <summary>这次升到的等级（可能连升几级）。</summary>
        public List<KeyValuePair<string, int>> Ups = new List<KeyValuePair<string, int>>();
        /// <summary>这次到达的精通阶。</summary>
        public List<KeyValuePair<string, long>> MasteryUps = new List<KeyValuePair<string, long>>();
    }

    /// <summary>
    /// 原 JS 的纯函数（logic.js + constants.js 里不依赖 store 的那些），逐行移植。
    /// 运算顺序刻意和 JS 保持一致（浮点乘法不满足结合律，换个顺序最后一位就可能不同）。
    /// </summary>
    public static class PetRules
    {
        public static double Clamp(double value, double low, double high)
        {
            return value < low ? low : value > high ? high : value;
        }

        public static int Clamp(int value, int low, int high)
        {
            return value < low ? low : value > high ? high : value;
        }

        /// <summary>连击数 → 经验倍率（1 + 0.2 × count）。</summary>
        public static double MultiplierOf(int count)
        {
            return 1 + 0.2 * count;
        }

        /// <summary>token 量级 → 食物量主项：token 每翻一倍多给 foodPerDouble 份（对数曲线）。</summary>
        public static double FoodFromTokens(long tokens, PetConfig config)
        {
            var ratio = 1 + Math.Max(0, tokens) / config.TokensPerFood;
            return Math.Floor(config.FoodPerDouble * JsMath.Log2(ratio) + 0.5);
        }

        /// <summary>token 量级 → 食物档位 tiny / small / large / feast。</summary>
        public static string FoodTierOf(long tokens)
        {
            if (tokens >= 5000) return "feast";
            if (tokens >= 800) return "large";
            if (tokens >= 120) return "small";
            return "tiny";
        }

        /// <summary>token 数 → 紧凑文本（999 / 1.2k / 12.3k / 1.2M），整千整万不留 ".0"。</summary>
        public static string FormatTokens(long tokens)
        {
            if (tokens < 1000) return tokens.ToString(CultureInfo.InvariantCulture);
            var small = tokens < 999950;
            var unit = small ? "k" : "M";
            var scaled = tokens / (small ? 1000.0 : 1000000.0);
            var text = ToFixed1(scaled);
            if (text.EndsWith(".0", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 2);
            return text + unit;
        }

        /// <summary>
        /// JS 的 Number.prototype.toFixed(1)（非负数）：按 double 的**精确值**四舍五入（平局取大）。
        /// 不能先乘 10 再取整（1.15 实际是 1.1499…，乘 10 会被舍成 11.5），也不能借 decimal（它只留 15 位）。
        /// </summary>
        internal static string ToFixed1(double x)
        {
            var bits = BitConverter.DoubleToInt64Bits(x);
            var exponent = (int)((bits >> 52) & 0x7FF);
            var mantissa = bits & 0xFFFFFFFFFFFFFL;
            if (exponent == 0) exponent = 1;
            else mantissa |= 1L << 52;
            exponent -= 1075;
            // x = mantissa × 2^exponent；求 n = floor(10x + 1/2)。
            BigInteger n;
            if (exponent >= 0)
            {
                n = new BigInteger(mantissa) * 10 * BigInteger.Pow(2, exponent);
            }
            else
            {
                var shift = -exponent;
                n = (new BigInteger(mantissa) * 10 + BigInteger.Pow(2, shift - 1)) >> shift;
            }
            var whole = BigInteger.Divide(n, 10);
            var tenth = (int)(n % 10);
            return whole.ToString(CultureInfo.InvariantCulture) + "." + tenth.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>连击数 → 视觉等级 normal / gold / epic。</summary>
        public static string TierOf(int count)
        {
            if (count >= 7) return "epic";
            if (count >= 4) return "gold";
            return "normal";
        }

        /// <summary>心情 → 经验倍率：0 心情 ×(1 - swing/2)，满心情 ×(1 + swing/2)。</summary>
        public static double MoodFactorOf(PetStats pet, PetConfig config)
        {
            if (!config.VitalsEnabled) return 1;
            return 1 - config.MoodExpSwing / 2 + config.MoodExpSwing * (pet.Mood / 100.0);
        }

        /// <summary>心情 + 精力合起来的经验倍率；饿惨了（心情见底且饿着）直接冻结。</summary>
        public static double VitalFactorOf(PetStats pet, PetConfig config)
        {
            if (!config.VitalsEnabled) return 1;
            if (pet.Mood <= config.MoodExpFreezeAt && pet.Hunger >= config.HungryAt) return 0;
            var tired = pet.Energy < config.LowEnergyAt ? config.TiredExpFactor : 1;
            return MoodFactorOf(pet, config) * tired;
        }

        /// <summary>epoch ms → 本地日序号（按本地时区切日，相邻两天差 1）。</summary>
        public static long DayIndexOf(long now, TimeZoneInfo zone)
        {
            var offset = zone.GetUtcOffset(DateTimeOffset.FromUnixTimeMilliseconds(now));
            return (long)Math.Floor((now + offset.TotalMilliseconds) / 86400000.0);
        }

        /// <summary>epoch ms → 本地小时（JS 的 new Date(now).getHours()）。</summary>
        public static int LocalHourOf(long now, TimeZoneInfo zone)
        {
            return TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(now), zone).Hour;
        }

        /// <summary>
        /// 喂一次食：hunger 下降、exp 累积、够了就升级（可能连升），顺带动心情 / 精力。返回新对象。
        /// </summary>
        public static PetStats FeedPet(PetData data, PetStats pet, int food, int exp, int moodDelta = 0, int energyDelta = 0)
        {
            var next = pet.Clone();
            next.Mood = Clamp(pet.Mood + moodDelta, 0, 100);
            next.Hunger = Math.Max(0, pet.Hunger - food);
            next.Energy = Clamp(pet.Energy + energyDelta, 0, 100);
            next.Exp = pet.Exp + exp;
            while (next.Exp >= next.Level * data.ExpPerLevel)
            {
                next.Exp -= next.Level * data.ExpPerLevel;
                next.Level += 1;
            }
            return next;
        }

        /// <summary>技能表：存档里有的行原样收下，缺的补一行 Lv.1。</summary>
        public static SkillTable CreateSkills(PetData data, SkillTable saved)
        {
            var skills = new SkillTable();
            foreach (var item in data.Skills)
            {
                var from = saved?[item.Key];
                skills.Rows[item.Key] = from ?? new SkillRecord { Xp = 0, Level = 1, Mastery = 0 };
            }
            return skills;
        }

        /// <summary>一份空记忆：还没见过任何文件 / 工具，从今天开始相处。</summary>
        public static PetMemory CreateMemory(long today)
        {
            return new PetMemory { BornDay = today };
        }

        /// <summary>相处了几天（第一天就是 1 天）。</summary>
        public static long TogetherDaysOf(PetMemory memory, long now, TimeZoneInfo zone)
        {
            return Math.Max(1, DayIndexOf(now, zone) - memory.BornDay + 1);
        }

        /// <summary>工具名 → 技能 key；不认识的算探索。</summary>
        public static string SkillOf(PetData data, string toolName)
        {
            return toolName != null && data.ToolSkill.TryGetValue(toolName, out var key) ? key : data.SkillFallback;
        }

        public static long SkillNeedOf(long level, PetConfig config)
        {
            return level * config.SkillXpPerLevel;
        }

        public static long MasteryTierOf(long mastery, PetConfig config)
        {
            if (config.MasteryXpPerTier <= 0) return 0;
            return (long)Math.Floor(mastery / (double)config.MasteryXpPerTier);
        }

        /// <summary>精通经验 → 「★★」；超过 5 阶写成「★★★★★+2」。</summary>
        public static string MasteryStarsOf(long mastery, PetConfig config)
        {
            var tier = MasteryTierOf(mastery, config);
            if (tier <= 0) return "";
            var stars = new string('★', (int)Math.Min(tier, 5));
            if (tier > 5) stars += "+" + (tier - 5).ToString(CultureInfo.InvariantCulture);
            return stars;
        }

        /// <summary>
        /// 给一条技能加经验，够了就升级；满级后溢出的经验转入精通。
        /// 没变（没这条技能 / xp ≤ 0）时 Skills 是原引用。
        /// </summary>
        public static SkillGain GainSkill(SkillTable skills, string key, long xp, PetConfig config)
        {
            var from = skills[key];
            var result = new SkillGain { Skills = skills };
            if (from == null || xp <= 0) return result;
            long level = from.Level;
            var have = from.Xp + xp;
            while (level < config.SkillMaxLevel && have >= SkillNeedOf(level, config))
            {
                have -= SkillNeedOf(level, config);
                level += 1;
                result.Ups.Add(new KeyValuePair<string, int>(key, (int)level));
            }
            var mastery = from.Mastery;
            if (level >= config.SkillMaxLevel)
            {
                var cap = SkillNeedOf(level, config) - 1;
                var overflow = have - cap;
                if (overflow > 0)
                {
                    var oldTier = MasteryTierOf(mastery, config);
                    mastery += overflow;
                    var newTier = MasteryTierOf(mastery, config);
                    for (var t = oldTier + 1; t <= newTier; t++) result.MasteryUps.Add(new KeyValuePair<string, long>(key, t));
                    have = cap;
                }
            }
            var next = skills.Clone();
            next.Rows[key] = new SkillRecord { Xp = (int)have, Level = (int)level, Mastery = mastery };
            result.Skills = next;
            return result;
        }

        /// <summary>
        /// 记忆表（文件 / 工具）+1：按次数降序（稳定排序），超容量时挤掉同分里最靠前的那个（最久没碰过的）。
        /// </summary>
        public static List<MemoryRow> BumpCount(List<MemoryRow> rows, string name, int cap)
        {
            var next = new List<MemoryRow>(rows.Count + 1);
            var found = false;
            foreach (var row in rows)
            {
                if (row.Name == name)
                {
                    next.Add(new MemoryRow(name, row.Count + 1));
                    found = true;
                }
                else
                {
                    next.Add(new MemoryRow(row.Name, row.Count));
                }
            }
            if (!found) next.Add(new MemoryRow(name, 1));
            StableSortDescending(next);
            while (next.Count > cap)
            {
                var min = next[next.Count - 1].Count;
                var at = next.Count - 1;
                while (at > 0 && next[at - 1].Count == min) at -= 1;
                next.RemoveAt(at);
            }
            return next;
        }

        /// <summary>按 Count 降序的稳定排序（JS 的 Array.prototype.sort 是稳定的，List.Sort 不是）。</summary>
        internal static void StableSortDescending(List<MemoryRow> rows)
        {
            for (var i = 1; i < rows.Count; i++)
            {
                var item = rows[i];
                var j = i - 1;
                while (j >= 0 && rows[j].Count < item.Count)
                {
                    rows[j + 1] = rows[j];
                    j--;
                }
                rows[j + 1] = item;
            }
        }

        public static bool HasRow(List<MemoryRow> rows, string name)
        {
            foreach (var row in rows)
            {
                if (row.Name == name) return true;
            }
            return false;
        }

        /// <summary>路径 → 文件名（截到 40 字）；取不出来就 null。</summary>
        public static string BaseNameOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var parts = path.Split('\\', '/');
            for (var i = parts.Length - 1; i >= 0; i--)
            {
                if (parts[i] != "") return parts[i].Length > 40 ? parts[i].Substring(0, 40) : parts[i];
            }
            return null;
        }

        /// <summary>活动直方图 → 最热的三小时（环形窗口），「21-23」；一次活动都没有则 null。</summary>
        public static string BusyHoursOf(long[] hours)
        {
            long best = 0;
            var at = -1;
            for (var start = 0; start < 24; start++)
            {
                var sum = hours[start] + hours[(start + 1) % 24] + hours[(start + 2) % 24];
                if (sum > best)
                {
                    best = sum;
                    at = start;
                }
            }
            if (at < 0) return null;
            return at.ToString(CultureInfo.InvariantCulture) + "-" + ((at + 2) % 24).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>离开了多久 → 「40 分钟」/「8 小时」/「3 天」（只取一个量级）。</summary>
        public static string AwayTextOf(long ms)
        {
            var hours = (long)Math.Floor(ms / 3600000.0);
            if (hours < 1) return Math.Max(1, (long)Math.Floor(ms / 60000.0)).ToString(CultureInfo.InvariantCulture) + " 分钟";
            if (hours < 24) return hours.ToString(CultureInfo.InvariantCulture) + " 小时";
            return ((long)Math.Floor(hours / 24.0)).ToString(CultureInfo.InvariantCulture) + " 天";
        }

        /// <summary>现在算不算深夜；from &gt; to 表示跨午夜。</summary>
        public static bool IsNightHour(int hour, int from, int to)
        {
            if (from <= to) return hour >= from && hour <= to;
            return hour >= from || hour <= to;
        }

        /// <summary>等级 → 形态档（等级不封顶，最高档是「≥ minLevel」）。</summary>
        public static StageDef WhaleStageOf(PetData data, int level)
        {
            var stage = data.WhaleStages[0];
            for (var i = 1; i < data.WhaleStages.Count; i++)
            {
                if (level >= data.WhaleStages[i].MinLevel) stage = data.WhaleStages[i];
            }
            return stage;
        }

        /// <summary>下一档形态；已经是最高档则 null。</summary>
        public static StageDef WhaleStageNextOf(PetData data, int level)
        {
            foreach (var stage in data.WhaleStages)
            {
                if (level < stage.MinLevel) return stage;
            }
            return null;
        }

        /// <summary>形态 key → 形态；认不出来就 null。</summary>
        public static FormDef FormOf(PetData data, string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return data.FormByKey.TryGetValue(key, out var form) ? form : null;
        }

        /// <summary>
        /// 该不该进化、进成哪一种：等级够，且有一门技能是**唯一**的最高分并到了门槛。一次定终身。
        /// </summary>
        public static FormDef EvolveTargetOf(PetData data, PetStats pet, SkillTable skills, PetConfig config)
        {
            if (!config.EvolveEnabled) return null;
            if (!string.IsNullOrEmpty(pet.Form)) return null;
            if (pet.Level < config.EvolveMinLevel) return null;
            if (skills == null) return null;
            SkillDef best = null;
            var bestLevel = 0;
            var tied = false;
            foreach (var item in data.Skills)
            {
                var row = skills[item.Key];
                var level = row == null ? 0 : row.Level;
                if (level > bestLevel)
                {
                    best = item;
                    bestLevel = level;
                    tied = false;
                }
                else if (level == bestLevel) tied = true;
            }
            if (best == null || tied) return null;
            if (bestLevel < config.EvolveMinSkillLevel) return null;
            return data.FormBySkill.TryGetValue(best.Key, out var form) ? form : null;
        }

        /// <summary>按日序号从任务池里挑当天的任务（确定性伪随机，同一天结果相同）。</summary>
        public static List<QuestDef> PickDailyQuests(PetData data, long dayIndex)
        {
            var pool = new List<QuestDef>(data.QuestPool);
            var picked = new List<QuestDef>();
            var seed = JsMath.ToUint32(dayIndex);
            for (var i = 0; i < data.DailyQuestCount && pool.Count > 0; i++)
            {
                seed = JsMath.Fmix32(seed);
                var idx = (int)(seed % (uint)pool.Count);
                picked.Add(pool[idx]);
                pool.RemoveAt(idx);
            }
            return picked;
        }

        /// <summary>
        /// 从台词池里挑一句的下标：seq 过一遍 fmix32 打散，再从「除了上一句之外」的里面挑。
        /// 同一串事件重跑说的还是那几句，同一句也不会连着出现两次。
        /// </summary>
        public static int PickLineIndex(int length, long seq, int last)
        {
            if (length <= 1) return 0;
            var h = JsMath.Fmix32(unchecked((uint)(int)seq));
            if (last < 0 || last >= length) return (int)(h % (uint)length);
            var pick = (int)(h % (uint)(length - 1));
            return pick >= last ? pick + 1 : pick;
        }

        /// <summary>心情 → 脸。</summary>
        public static string MoodFaceOf(int mood)
        {
            if (mood >= 80) return "😍";
            if (mood >= 50) return "🙂";
            if (mood >= 30) return "😐";
            return "😢";
        }

        /// <summary>此刻写在脸上的那一维（最高且越过 moodDimAt；同分取靠前的）；没有就 null。</summary>
        public static MoodDimDef MoodDimOf(PetData data, PetStats pet, PetConfig config)
        {
            if (!config.MoodDimsEnabled) return null;
            MoodDimDef best = null;
            var bestValue = config.MoodDimAt;
            foreach (var dim in data.MoodDims)
            {
                var value = pet.Dim(dim.Key);
                if (value >= bestValue && (best == null || value > bestValue))
                {
                    best = dim;
                    bestValue = value;
                }
            }
            return best;
        }

        /// <summary>多宠物的展示顺序：收藏的在前（按收藏顺序），其余按出生时间。</summary>
        public static List<string> ComputePetOrder(PetCollection collection, List<string> favorites)
        {
            var ids = new List<string>(collection.Ids);
            var fav = new Dictionary<string, int>();
            for (var i = 0; i < favorites.Count; i++) fav[favorites[i]] = i;
            Comparison<string> compare = (a, b) =>
            {
                var aFav = fav.TryGetValue(a, out var ai);
                var bFav = fav.TryGetValue(b, out var bi);
                if (aFav && !bFav) return -1;
                if (!aFav && bFav) return 1;
                if (aFav && bFav) return ai.CompareTo(bi);
                return collection[a].BornAt.CompareTo(collection[b].BornAt);
            };
            // 稳定的插入排序（同 bornAt 时保持集合的插入顺序，和 JS 一致）。
            for (var i = 1; i < ids.Count; i++)
            {
                var item = ids[i];
                var j = i - 1;
                while (j >= 0 && compare(ids[j], item) > 0)
                {
                    ids[j + 1] = ids[j];
                    j--;
                }
                ids[j + 1] = item;
            }
            return ids;
        }
    }
}
