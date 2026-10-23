using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DeepseekPet.Core;
using Newtonsoft.Json.Linq;

namespace DeepseekPet.Replay
{
    /// <summary>手拨的时钟。</summary>
    public sealed class ManualClock : IClock
    {
        public long Now { get; set; }
    }

    /// <summary>
    /// 轨迹里的一个剧本：时区、随机种子、配置覆盖、初始存档，和一串（时刻, 操作, 参数）。
    /// PetTools 用它驱动 Jint 里的原 JS 生成金样；测试用它驱动 C# 的 PetCore 回放比对。
    /// </summary>
    public sealed class TraceScript
    {
        public string Name;
        public int TzOffsetMinutes;
        public uint Seed;
        public JObject Overrides;
        public string InitialSave;
        /// <summary>开机时刻（epoch ms）。</summary>
        public long BootAt;
        public List<TraceStep> Steps = new List<TraceStep>();

        public TimeZoneInfo Zone()
        {
            return ZoneOf(TzOffsetMinutes);
        }

        public static TimeZoneInfo ZoneOf(int offsetMinutes)
        {
            var offset = TimeSpan.FromMinutes(offsetMinutes);
            return TimeZoneInfo.CreateCustomTimeZone("pet" + offsetMinutes.ToString(CultureInfo.InvariantCulture), offset, "pet", "pet");
        }
    }

    public sealed class TraceStep
    {
        public long Now;
        public string Op;
        public JToken A;
        public JToken B;
        /// <summary>金样：操作的返回值和之后的快照（只在 gen-traces 写出的文件里有）。</summary>
        public JToken Ret;
        public JObject Snap;
    }

    /// <summary>C# 侧：按剧本驱动 PetCore，给出和 api.js 的 __snap 同形状的快照。</summary>
    public sealed class CoreRunner
    {
        readonly PetData _data;
        readonly TraceScript _script;
        readonly ManualClock _clock = new ManualClock();
        readonly MemoryStorage _storage;
        readonly Mulberry32 _random;
        readonly PetConfig _config;

        public PetCore Core { get; private set; }

        public CoreRunner(PetData data, TraceScript script)
        {
            _data = data;
            _script = script;
            _storage = new MemoryStorage(script.InitialSave);
            _random = new Mulberry32(script.Seed);
            _config = PetConfig.Create(data, script.Overrides);
            _clock.Now = script.BootAt;
            Core = new PetCore(data, _config, _clock, _random, _storage, script.Zone());
        }

        public string Storage => _storage.Content;

        public JToken Run(TraceStep step)
        {
            _clock.Now = step.Now;
            Core.Update();
            var c = Core;
            switch (step.Op)
            {
                case "event":
                {
                    var a = (JObject)step.A;
                    c.HandleEvent(a.Value<string>("kind"), a.Value<long?>("tokens") ?? 0, a.Value<long?>("output") ?? 0,
                        a.Value<string>("tool"), a.Value<string>("callId"), a.Value<string>("file"), a.Value<bool?>("failed") ?? false);
                    return JValue.CreateNull();
                }
                case "advance": return JValue.CreateNull();
                case "tick": c.Tick(); return JValue.CreateNull();
                case "snack": return c.Snack();
                case "pat": return c.Pat();
                case "prestige": return c.Prestige();
                case "switchForm": return c.SwitchForm(step.A.Value<string>());
                case "switchPet": c.SwitchPet(step.A.Value<int>()); return JValue.CreateNull();
                case "toggleFavorite": c.ToggleFavorite(); return JValue.CreateNull();
                case "addPet": return c.AddPet(step.A.Value<string>(), step.B?.Value<string>());
                case "renamePet": return c.RenamePet(step.A.Value<string>());
                case "hatchFirst":
                {
                    if (c.EggList.Count == 0) return false;
                    c.StartHatch(c.EggList[0].Id);
                    return c.ConfirmHatch(step.A?.Value<string>());
                }
                case "minigame.start": return c.StartMiniGame();
                case "minigame.hit": c.HitMiniGameTarget(c.State.MiniGame == null ? 0 : c.State.MiniGame.Current); return JValue.CreateNull();
                case "minigame.fail": c.FailMiniGame(); return JValue.CreateNull();
                case "gm.setLevel": c.GmSetLevel(step.A.Value<double>()); return JValue.CreateNull();
                case "gm.addExp": c.GmAddExp(step.A.Value<double>()); return JValue.CreateNull();
                case "gm.setHunger": c.GmSetHunger(step.A.Value<double>()); return JValue.CreateNull();
                case "gm.setMood": c.GmSetMood(step.A.Value<double>()); return JValue.CreateNull();
                case "gm.setEnergy": c.GmSetEnergy(step.A.Value<double>()); return JValue.CreateNull();
                case "gm.fillSnacks": c.GmFillSnacks(); return JValue.CreateNull();
                case "gm.grantEggs": c.GmGrantEggs(); return JValue.CreateNull();
                case "gm.simulateFeed": c.GmSimulateFeed(step.A.Value<long>()); return JValue.CreateNull();
                case "reboot":
                    // 等同网页刷新：落盘、所有定时器作废，再从存档起一只。
                    c.Dispose();
                    // 带参数 = 关掉之后过了这么久才重新打开。
                    if (step.A != null && step.A.Type == JTokenType.Integer) _clock.Now += step.A.Value<long>();
                    Core = new PetCore(_data, _config, _clock, _random, _storage, _script.Zone());
                    return JValue.CreateNull();
                default:
                    throw new ArgumentException("未知操作 " + step.Op);
            }
        }

        static JArray Rows(List<MemoryRow> rows)
        {
            return new JArray(rows.Select(r => new JObject { ["name"] = r.Name, ["count"] = r.Count }));
        }

        /// <summary>和 api.js 的 __snap 一一对应。</summary>
        public JObject Snap()
        {
            var s = Core.State;
            var p = s.Pet;
            var skills = new JObject();
            foreach (var item in _data.Skills)
            {
                var row = s.Skills[item.Key];
                skills[item.Key] = new JObject { ["xp"] = row.Xp, ["level"] = row.Level, ["mastery"] = row.Mastery };
            }
            var m = s.Memory;
            var d = s.Daily;
            var g = Core.GlobalStats;
            var col = Core.Collection;
            return new JObject
            {
                ["pet"] = new JObject
                {
                    ["name"] = p.Name, ["species"] = p.Species, ["avatar"] = p.Avatar, ["icon"] = p.Icon,
                    ["mood"] = p.Mood, ["hunger"] = p.Hunger, ["energy"] = p.Energy,
                    ["curiosity"] = p.Curiosity, ["pride"] = p.Pride, ["concern"] = p.Concern,
                    ["exp"] = p.Exp, ["level"] = p.Level, ["form"] = p.Form,
                },
                ["comboCount"] = s.ComboCount,
                ["comboMultiplier"] = s.ComboMultiplier,
                ["comboTier"] = s.ComboTier,
                ["eatKey"] = s.EatKey,
                ["patKey"] = s.PatKey,
                ["lastAct"] = s.LastAct,
                ["idleAct"] = s.IdleAct,
                ["totalFeeds"] = s.TotalFeeds,
                ["totalTokens"] = s.TotalTokens,
                ["tokensBySource"] = new JObject
                {
                    ["user_input"] = s.TokensBySource.UserInput, ["generation"] = s.TokensBySource.Generation, ["tool_result"] = s.TokensBySource.ToolResult,
                },
                ["snacks"] = s.Snacks,
                ["achievements"] = new JArray(s.Achievements),
                ["daily"] = new JObject
                {
                    ["day"] = d.Day, ["feeds"] = d.Feeds, ["tools"] = d.Tools, ["bestCombo"] = d.BestCombo,
                    ["pats"] = d.Pats, ["tokens"] = d.Tokens, ["snacks"] = d.Snacks, ["done"] = new JArray(d.Done),
                },
                ["streakDay"] = s.StreakDay,
                ["streakCount"] = s.StreakCount,
                ["pats"] = s.Pats,
                ["skills"] = skills,
                ["memory"] = new JObject
                {
                    ["files"] = Rows(m.Files), ["tools"] = Rows(m.Tools), ["hours"] = new JArray(m.Hours),
                    ["bornDay"] = m.BornDay, ["errors"] = m.Errors, ["recoveries"] = m.Recoveries,
                },
                ["lastFeedAt"] = s.LastFeedAt,
                ["asleep"] = s.Asleep,
                ["bubble"] = s.Bubble == null ? JValue.CreateNull() : new JObject { ["key"] = s.Bubble.Key, ["kind"] = s.Bubble.Kind, ["text"] = s.Bubble.Text },
                ["buff"] = s.Buff == null ? JValue.CreateNull() : new JObject { ["kind"] = s.Buff.Kind, ["until"] = s.Buff.Until },
                ["effects"] = new JArray(s.Effects.Select(e => new JObject
                {
                    ["key"] = e.Key, ["icon"] = e.Icon, ["text"] = e.Text, ["foodAmount"] = e.FoodAmount, ["expAmount"] = e.ExpAmount,
                    ["tokens"] = e.Tokens, ["foodTier"] = e.FoodTier, ["source"] = e.Source, ["comboCount"] = e.ComboCount,
                    ["comboMultiplier"] = e.ComboMultiplier, ["tier"] = e.Tier,
                })),
                ["miniGame"] = s.MiniGame == null ? JValue.CreateNull() : new JObject
                {
                    ["targets"] = new JArray(s.MiniGame.Targets.Select(t => new JObject { ["id"] = t.Id, ["icon"] = t.Icon, ["x"] = t.X, ["y"] = t.Y, ["caught"] = t.Caught })),
                    ["current"] = s.MiniGame.Current, ["startedAt"] = s.MiniGame.StartedAt, ["deadline"] = s.MiniGame.Deadline,
                    ["score"] = s.MiniGame.Score, ["finished"] = s.MiniGame.Finished, ["success"] = s.MiniGame.Success,
                },
                ["hatching"] = s.HatchingEgg?.Type,
                ["eggs"] = new JArray(s.Eggs.Select(e => new JObject { ["type"] = e.Type, ["obtainedAt"] = e.ObtainedAt })),
                ["talent"] = s.Talent,
                ["prestige"] = s.Prestige,
                ["formsOwned"] = new JArray(s.FormsOwned),
                ["petCount"] = s.PetCount,
                ["activeIndex"] = s.PetOrder.IndexOf(s.ActivePetId),
                ["favorites"] = new JArray(s.Favorites.Select(id => s.PetOrder.IndexOf(id))),
                ["pets"] = new JArray(s.PetOrder.Select(id =>
                {
                    var r = col[id];
                    return new JObject { ["species"] = r.Species, ["name"] = r.Name, ["level"] = r.Pet.Level, ["prestige"] = r.Prestige, ["bornAt"] = r.BornAt };
                })),
                ["global"] = new JObject
                {
                    ["totalTokensAllTime"] = g.TotalTokensAllTime,
                    ["totalFeedsAllTime"] = g.TotalFeedsAllTime,
                    ["achievementsUnlockedAllTime"] = g.AchievementsUnlockedAllTime,
                    ["petsHatched"] = g.PetsHatched,
                    ["eggsObtained"] = new JArray(g.EggsObtained),
                },
            };
        }
    }

    /// <summary>逐字段比对两份 JSON：数字按数值比（JS 的 82 和 C# 的 82.0 是一回事），找到第一处不同就给出路径。</summary>
    public static class JsonDiff
    {
        public static string FirstDifference(JToken expected, JToken actual, string path = "$")
        {
            if (IsNumber(expected) && IsNumber(actual))
            {
                return expected.Value<double>() == actual.Value<double>() ? null : path + ": 期望 " + Show(expected) + "，实际 " + Show(actual);
            }
            if (IsNull(expected) && IsNull(actual)) return null;
            if (expected == null || actual == null || expected.Type != actual.Type || IsNull(expected) || IsNull(actual))
            {
                return path + ": 期望 " + Show(expected) + "，实际 " + Show(actual);
            }
            if (expected is JObject eo)
            {
                var ao = (JObject)actual;
                foreach (var prop in eo.Properties())
                {
                    if (!ao.TryGetValue(prop.Name, out var av)) return path + "." + prop.Name + ": C# 侧没有这个字段";
                    var diff = FirstDifference(prop.Value, av, path + "." + prop.Name);
                    if (diff != null) return diff;
                }
                foreach (var prop in ao.Properties())
                {
                    if (!eo.ContainsKey(prop.Name)) return path + "." + prop.Name + ": JS 侧没有这个字段";
                }
                return null;
            }
            if (expected is JArray ea)
            {
                var aa = (JArray)actual;
                for (var i = 0; i < Math.Min(ea.Count, aa.Count); i++)
                {
                    var diff = FirstDifference(ea[i], aa[i], path + "[" + i + "]");
                    if (diff != null) return diff;
                }
                return ea.Count == aa.Count ? null : path + ": 期望 " + ea.Count + " 项，实际 " + aa.Count + " 项";
            }
            return JToken.DeepEquals(expected, actual) ? null : path + ": 期望 " + Show(expected) + "，实际 " + Show(actual);
        }

        static bool IsNumber(JToken t)
        {
            return t != null && (t.Type == JTokenType.Integer || t.Type == JTokenType.Float);
        }

        static bool IsNull(JToken t)
        {
            return t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Undefined || (t is JValue v && v.Value == null);
        }

        static string Show(JToken t)
        {
            if (t == null) return "(无)";
            var text = t.ToString(Newtonsoft.Json.Formatting.None);
            return text.Length > 200 ? text.Substring(0, 200) + "…" : text;
        }

        /// <summary>快照增量：只留和上一份不同的顶层字段（轨迹文件靠它瘦身）。</summary>
        public static JObject Delta(JObject previous, JObject current)
        {
            var delta = new JObject();
            foreach (var prop in current.Properties())
            {
                var before = previous?[prop.Name];
                if (before == null || FirstDifference(before, prop.Value) != null) delta[prop.Name] = prop.Value.DeepClone();
            }
            return delta;
        }

        /// <summary>把增量叠回上一份完整快照。</summary>
        public static JObject Apply(JObject previous, JObject delta)
        {
            var next = previous == null ? new JObject() : (JObject)previous.DeepClone();
            foreach (var prop in delta.Properties()) next[prop.Name] = prop.Value.DeepClone();
            return next;
        }
    }
}
