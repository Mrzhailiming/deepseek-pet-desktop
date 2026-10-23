using System;
using System.Collections.Generic;
using System.Globalization;

namespace DeepseekPet.Core
{
    /// <summary>成就判定看的统计快照（JS store 的 statsOf）。</summary>
    public sealed class AchievementStats
    {
        public PetData Data;
        public PetStats Pet;
        public long TotalFeeds;
        public long TotalTokens;
        public TokensBySource TokensBySource;
        public long Pats;
        public long StreakCount;
        public SkillTable Skills;
        public PetMemory Memory;
        public int ComboCount;
        /// <summary>这一口的食物档位；不是喂食时为 null。</summary>
        public string FoodTier;
        public int FormsOwnedCount;
    }

    /// <summary>动态台词模板的上下文（JS store 的 buildDynamicCtx）。</summary>
    public sealed class DynamicLineContext
    {
        public PetData Data;
        public PetStats Pet;
        public long TotalFeeds;
        public long TotalTokens;
        public long TogetherDays;
        public long Pats;
        public long StreakCount;
        public PetMemory Memory;
        public string LastToolName;
        public string LastSkillUp;
        public SkillTable Skills;
        public int Prestige;
    }

    /// <summary>
    /// 原 JS 里写成函数的那几张表（constants.js / config.js）：成就判定、任务进度、蛋里程碑、称号、
    /// 动态台词 / 提示 / 闲聊模板。数据部分在 pet-data.json 里，这里按 id 一一对上。
    /// </summary>
    public static class PetTables
    {
        static string N(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        static readonly Dictionary<string, Func<AchievementStats, PetConfig, bool>> AchievementTests = new Dictionary<string, Func<AchievementStats, PetConfig, bool>>
        {
            ["first_feed"] = (s, c) => s.TotalFeeds >= 1,
            ["gourmet"] = (s, c) => s.TokensBySource.UserInput > 0 && s.TokensBySource.Generation > 0 && s.TokensBySource.ToolResult > 0,
            ["combo_full"] = (s, c) => s.ComboCount >= c.MaxCombo,
            ["feast"] = (s, c) => s.FoodTier == "feast",
            ["tokens_100k"] = (s, c) => s.TotalTokens >= 100000,
            ["tokens_1m"] = (s, c) => s.TotalTokens >= 1000000,
            ["feeds_100"] = (s, c) => s.TotalFeeds >= 100,
            ["pats_50"] = (s, c) => s.Pats >= 50,
            ["streak_3"] = (s, c) => s.StreakCount >= 3,
            ["level_legend"] = (s, c) => s.Pet.Level >= 10,
            ["streak_7"] = (s, c) => s.StreakCount >= 7,
            ["skill_master"] = (s, c) =>
            {
                foreach (var skill in s.Data.Skills)
                {
                    var row = s.Skills[skill.Key];
                    if (row != null && row.Level >= c.SkillMaxLevel) return true;
                }
                return false;
            },
            ["recover_50"] = (s, c) => s.Memory.Recoveries >= 50,
            ["curious"] = (s, c) => s.Pet.Curiosity >= 100,
            ["level_15"] = (s, c) => s.Pet.Level >= 15,
            ["level_20"] = (s, c) => s.Pet.Level >= 20,
            ["level_25"] = (s, c) => s.Pet.Level >= 25,
            ["form_collector"] = (s, c) => s.FormsOwnedCount >= 2,
            ["form_all"] = (s, c) => s.FormsOwnedCount >= 4,
        };

        static readonly Dictionary<string, Func<DailyProgress, long>> QuestProgress = new Dictionary<string, Func<DailyProgress, long>>
        {
            ["feeds"] = d => d.Feeds,
            ["tools"] = d => d.Tools,
            ["combo"] = d => d.BestCombo,
            ["feeds20"] = d => d.Feeds,
            ["feeds5"] = d => d.Feeds,
            ["pats5"] = d => d.Pats,
            ["pats15"] = d => d.Pats,
            ["tok10k"] = d => d.Tokens,
            ["tok50k"] = d => d.Tokens,
            ["tools15"] = d => d.Tools,
            ["snacks3"] = d => d.Snacks,
            ["combo10"] = d => d.BestCombo,
        };

        static Dictionary<string, Func<GlobalStats, bool>> EggTests(int achievementCount)
        {
            return new Dictionary<string, Func<GlobalStats, bool>>
            {
                ["tok_50k"] = g => g.TotalTokensAllTime >= 50000,
                ["tok_200k"] = g => g.TotalTokensAllTime >= 200000,
                ["tok_500k"] = g => g.TotalTokensAllTime >= 500000,
                ["tok_1m"] = g => g.TotalTokensAllTime >= 1000000,
                ["tok_3m"] = g => g.TotalTokensAllTime >= 3000000,
                ["tok_5m"] = g => g.TotalTokensAllTime >= 5000000,
                ["conv_200"] = g => g.TotalFeedsAllTime >= 200,
                ["conv_500"] = g => g.TotalFeedsAllTime >= 500,
                ["conv_1000"] = g => g.TotalFeedsAllTime >= 1000,
                ["conv_2000"] = g => g.TotalFeedsAllTime >= 2000,
                ["ach_5"] = g => g.AchievementsUnlockedAllTime >= 5,
                ["ach_10"] = g => g.AchievementsUnlockedAllTime >= 10,
                // 门槛跟着成就表走：集齐全部才给彩虹蛋。
                ["ach_all"] = g => g.AchievementsUnlockedAllTime >= achievementCount,
                ["pets_3"] = g => g.PetsHatched >= 3,
                ["pets_5"] = g => g.PetsHatched >= 5,
                ["pets_10"] = g => g.PetsHatched >= 10,
            };
        }

        static readonly Dictionary<string, Func<int, int, int, bool>> TitleTests = new Dictionary<string, Func<int, int, int, bool>>
        {
            ["新手"] = (level, prestige, forms) => true,
            ["熟练"] = (level, prestige, forms) => level >= 5,
            ["老手"] = (level, prestige, forms) => level >= 10,
            ["精英"] = (level, prestige, forms) => level >= 15,
            ["老练"] = (level, prestige, forms) => level >= 20,
            ["大师"] = (level, prestige, forms) => level >= 25,
            ["宗师"] = (level, prestige, forms) => level >= 30,
            ["转世"] = (level, prestige, forms) => prestige >= 1,
            ["飞升"] = (level, prestige, forms) => prestige >= 2,
            ["不朽"] = (level, prestige, forms) => prestige >= 3,
            ["收藏家"] = (level, prestige, forms) => forms >= 2,
            ["全图鉴"] = (level, prestige, forms) => forms >= 4,
        };

        /// <summary>
        /// 动态台词（DYNAMIC_LINES）：场合 → 模板列表，模板返回 null 表示条件不满足。
        /// 顺序必须和 JS 一致：挑哪一个是按下标算的（pickLineIndex）。
        /// </summary>
        public static readonly Dictionary<string, Func<DynamicLineContext, string>[]> DynamicLines = new Dictionary<string, Func<DynamicLineContext, string>[]>
        {
            ["user_input"] = new Func<DynamicLineContext, string>[]
            {
                x => x.TotalFeeds < 10 ? null : "第 " + N(x.TotalFeeds) + " 次喂我了，谢谢你一直在",
                x => x.TogetherDays < 3 ? null : "我们在一起第 " + N(x.TogetherDays) + " 天了",
            },
            ["generation"] = new Func<DynamicLineContext, string>[]
            {
                x => x.TotalTokens < 10000 ? null : "累计吞了 " + PetRules.FormatTokens(x.TotalTokens) + " tok 了，肚量变大了",
                x => x.Pet.Level < 3 ? null : "Lv." + N(x.Pet.Level) + " 的我消化能力变强了",
            },
            ["tool_result"] = new Func<DynamicLineContext, string>[]
            {
                x => string.IsNullOrEmpty(x.LastToolName) ? null : x.LastToolName + " 的味道我记住了！",
                x => x.Memory.Tools.Count == 0 ? null : "我最常吃的是 " + x.Memory.Tools[0].Name + " 做的菜",
                x =>
                {
                    if (string.IsNullOrEmpty(x.LastToolName)) return null;
                    MemoryRow tool = null;
                    foreach (var row in x.Memory.Tools)
                    {
                        if (row.Name == x.LastToolName) { tool = row; break; }
                    }
                    if (tool == null || tool.Count < 5) return null;
                    return x.LastToolName + " 第 " + N(tool.Count) + " 次了，我闭着眼都认得出";
                },
            },
            ["pat"] = new Func<DynamicLineContext, string>[]
            {
                x => x.Pats < 20 ? null : "你已经摸了我 " + N(x.Pats) + " 次了哦",
                x => x.TogetherDays < 2 ? null : "每天都摸摸我好不好",
                x => x.Pet.Mood >= 90 ? "心情好到要飞起来了～" : null,
            },
            ["combo"] = new Func<DynamicLineContext, string>[]
            {
                x => x.StreakCount < 3 ? null : "连续第 " + N(x.StreakCount) + " 天了，节奏真好",
            },
            ["levelup"] = new Func<DynamicLineContext, string>[]
            {
                x => "Lv." + N(x.Pet.Level) + " 了！" + PetRules.WhaleStageOf(x.Data, x.Pet.Level).Label + "形态越来越强",
            },
            ["skill"] = new Func<DynamicLineContext, string>[]
            {
                x =>
                {
                    if (string.IsNullOrEmpty(x.LastSkillUp)) return null;
                    if (!x.Data.SkillById.TryGetValue(x.LastSkillUp, out var skill)) return null;
                    return skill.Label + "又进步了，继续加油！";
                },
            },
            ["hungry"] = new Func<DynamicLineContext, string>[]
            {
                x => x.Memory.Files.Count == 0 ? null : "饿着肚子看 " + x.Memory.Files[0].Name + " 好难受…",
            },
            ["wake"] = new Func<DynamicLineContext, string>[]
            {
                x => x.TogetherDays < 2 ? null : "新的一天！我们在一起 " + N(x.TogetherDays) + " 天了",
                x => x.Memory.Files.Count == 0 ? null : "醒了！今天还写 " + x.Memory.Files[0].Name + " 吗？",
            },
        };

        // ─── 提示（ADVICE_LINES）：往里填当下的事实 ───

        public static string RepeatEdit(string file, long count)
        {
            if (count >= 9) return file + " 已经第 " + N(count) + " 次了…要不先想清楚再动手？";
            if (count >= 6) return file + " 改了 " + N(count) + " 次还没好，是不是卡住了？";
            return file + " 改到第 " + N(count) + " 次了，跑个测试？";
        }

        public static string ErrorStreak(long count)
        {
            return "连着 " + N(count) + " 次报错了，先看看上一条错误？";
        }

        public static string ToolFlaky(string tool, long count)
        {
            return tool + " 连着挂了 " + N(count) + " 次，换个思路试试？";
        }

        public static string Recovered(string tool)
        {
            return tool + " 终于成了！这道坎跨过去了";
        }

        // ─── 闲聊（CHAT_LINES）：引用记忆，没有信息量，走长冷却 ───

        public static string Comeback(string text) { return "你去哪儿了…我等了 " + text; }
        public static string FavoriteFile(string file) { return "又是 " + file + " 啊"; }
        public static string ToolHabit(string tool, long count) { return "你已经用了 " + N(count) + " 次 " + tool + " 了"; }
        public static string BusyHour(string range) { return "又到你干活的点了（" + range + " 点）"; }
        public static string FileAgain(string file, long count) { return file + " 今天改了 " + N(count) + " 次了哦"; }
        public static string Streak(long days) { return "连续 " + N(days) + " 天都能见到你，好开心"; }
        public static string Together(long days) { return "不知不觉我们已经相处 " + N(days) + " 天了"; }
        public static string Growth(long level) { return "Lv." + N(level) + " 了，一起走了好远呢"; }
        public static string TokensMilestone(long tokens) { return "我们一共处理了 " + PetRules.FormatTokens(tokens) + " token，真是大工程"; }
        public static string SkillNote(string skill, long level) { return "你的" + skill + "已经 Lv." + N(level) + " 了，我跟着你学了好多"; }
        public static string FileExpert(string file, long count) { return file + " 改了 " + N(count) + " 次了，你是不是很在意这个文件？"; }
        public static string Resilience(long errors, long recoveries) { return "报过 " + N(errors) + " 次错，跨过 " + N(recoveries) + " 次…你很能扛呀"; }

        static readonly string[] AdviceKeys = { "repeat_edit", "error_streak", "tool_flaky", "recovered" };
        static readonly string[] ChatKeys =
        {
            "comeback", "favorite_file", "tool_habit", "busy_hour", "file_again", "streak", "together", "growth",
            "tokens_milestone", "skill_note", "file_expert", "resilience",
        };

        /// <summary>称号（TITLES）：取最后一个满足条件的。</summary>
        public static string TitleOf(PetData data, int level, int prestige, int formsCount)
        {
            var best = data.Titles[0];
            for (var i = 1; i < data.Titles.Count; i++)
            {
                if (TitleTests[data.Titles[i]](level, prestige, formsCount)) best = data.Titles[i];
            }
            return best;
        }

        /// <summary>把判定绑到数据表上；JS 那边多了 / 少了条目就报错，别悄悄地少一条成就。</summary>
        internal static void Bind(PetData data)
        {
            foreach (var a in data.Achievements)
            {
                if (!AchievementTests.TryGetValue(a.Id, out a.Test)) throw Missing("成就", a.Id);
            }
            foreach (var q in data.QuestPool)
            {
                if (!QuestProgress.TryGetValue(q.Id, out q.Of)) throw Missing("每日任务", q.Id);
            }
            var eggTests = EggTests(data.Achievements.Count);
            foreach (var m in data.EggMilestones)
            {
                if (!eggTests.TryGetValue(m.Id, out m.Test)) throw Missing("蛋里程碑", m.Id);
            }
            foreach (var t in data.Titles)
            {
                if (!TitleTests.ContainsKey(t)) throw Missing("称号", t);
            }
            foreach (var pair in data.DynamicLineCounts)
            {
                if (!DynamicLines.TryGetValue(pair.Key, out var lines) || lines.Length != pair.Value) throw Missing("动态台词", pair.Key);
            }
            if (DynamicLines.Count != data.DynamicLineCounts.Count) throw Missing("动态台词", "（C# 多了场合）");
            foreach (var key in data.AdviceLines)
            {
                if (Array.IndexOf(AdviceKeys, key) < 0) throw Missing("提示模板", key);
            }
            foreach (var key in data.ChatLines)
            {
                if (Array.IndexOf(ChatKeys, key) < 0) throw Missing("闲聊模板", key);
            }
        }

        static InvalidOperationException Missing(string table, string id)
        {
            return new InvalidOperationException("pet-data.json 的" + table + "「" + id + "」在 PetTables 里没有对应实现（JS 改过表？）");
        }
    }
}
