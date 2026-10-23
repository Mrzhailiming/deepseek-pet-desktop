using System;
using System.Collections.Generic;
using DeepseekPet.Replay;
using Newtonsoft.Json.Linq;

namespace DeepseekPet.PetTools
{
    /// <summary>
    /// 剧本生成器：用种子随机数拼出「像真人在用」的一串操作 —— 一轮轮 agent 活动（输入、工具调用 / 结果、生成），
    /// 中间穿插空闲（几秒到一天多）、摸头、零食、小游戏、多宠物、轮回、GM 改数值、重启。
    /// 同一个种子生成的剧本永远相同。
    /// </summary>
    static class Scenarios
    {
        static readonly string[] Tools = { "read", "edit", "write", "bash", "grep", "glob", "web_fetch", "todo_write", "subagent", "skill", "mcp__x", "edit", "read", "bash" };
        static readonly string[] Files = { "store.js", "views.js", "logic.js", "PetCore.cs", "README.md", "a.rs", "main.go", "index.ts", "x.py", "build.mjs", "app.json", "notes.md", "very-long-file-name-that-goes-on-and-on-forever.txt" };
        static readonly string[] Species = { "whale", "cat", "fox", "bird", "bug", "dragon" };
        static readonly string[] Forms = { "cat", "fox", "bug", "bird", "nope" };

        /// <summary>预设剧本（gen-traces 写成金样文件）。</summary>
        public static List<TraceScript> Presets()
        {
            return new List<TraceScript>
            {
                Random("default-cn", 1, 1600, 480, Local(2026, 10, 20, 7, 30, 480)),
                Random("night-owl", 2, 1200, 480, Local(2026, 10, 21, 0, 40, 480)),
                Random("utc-minus5", 3, 1200, -300, Local(2026, 12, 31, 22, 0, -300)),
                Random("knobs-off", 4, 1000, 480, Local(2026, 10, 20, 9, 0, 480), new JObject
                {
                    ["vitalsEnabled"] = false, ["pickyEnabled"] = false, ["dynamicLinesEvery"] = 0, ["effectsEnabled"] = false,
                    ["sleepEnabled"] = false, ["moodDimsEnabled"] = false, ["memoryEnabled"] = false, ["dailyEnabled"] = false,
                    ["achievementsEnabled"] = false, ["skillsEnabled"] = false, ["hungerRegenPerMin"] = 0, ["manualSnackRegenMs"] = 0,
                }),
                Random("fast-growth", 5, 1600, 480, Local(2026, 10, 20, 20, 0, 480), new JObject
                {
                    ["skillXpPerLevel"] = 2, ["masteryXpPerTier"] = 10, ["evolveMinLevel"] = 3, ["evolveMinSkillLevel"] = 3,
                    ["adviceMinSkillLevel"] = 1, ["adviceCooldownMs"] = 2000, ["careCooldownMs"] = 30000, ["careMarathonMs"] = 600000,
                    ["eggDropBaseChance"] = 0.08, ["bubbleMinGapMs"] = 500, ["prestigeMinLevel"] = 6, ["sleepAfterMs"] = 60000,
                    ["dynamicLinesEvery"] = 2, ["comboWindowMs"] = 8000, ["maxCombo"] = 6, ["boredomAfter"] = 3,
                    ["favoriteSource"] = "generation", ["persist"] = true, ["saveDebounceMs"] = 700,
                }),
                Random("import-v1", 6, 500, 480, Local(2026, 10, 22, 10, 0, 480), null, V1Save(Local(2026, 10, 21, 23, 0, 480))),
                Random("import-v2", 7, 700, 480, Local(2026, 10, 22, 10, 0, 480), null, V2Save(Local(2026, 10, 20, 8, 0, 480))),
                Milestones(),
            };
        }

        /// <summary>手写剧本：喂食正好跨过 Lv.15 / 20 / 25 / 30 的里程碑，然后轮回、轮回后再喂。随机剧本很少走到这里。</summary>
        static TraceScript Milestones()
        {
            var start = Local(2026, 10, 22, 15, 0, 480);
            var script = new TraceScript { Name = "milestones", Seed = 8, TzOffsetMinutes = 480, BootAt = start };
            var now = start;
            void Add(string op, JToken a = null)
            {
                now += 1200;
                script.Steps.Add(new TraceStep { Now = now, Op = op, A = a });
            }
            void Feed(int tokens)
            {
                Add("event", new JObject { ["kind"] = "tool_call", ["tool"] = "edit", ["callId"] = "m" + now, ["file"] = "PetCore.cs" });
                Add("event", new JObject { ["kind"] = "tool_result", ["callId"] = "m" + (now - 1200), ["tokens"] = tokens, ["failed"] = false });
                Add("event", new JObject { ["kind"] = "generation", ["tokens"] = tokens * 3, ["output"] = 900 });
            }
            foreach (var level in new[] { 14, 19, 24, 29 })
            {
                Add("gm.setLevel", level);
                Add("gm.addExp", level * 100 - 3);
                Feed(400);
                Feed(6000);
            }
            Add("prestige");
            Feed(800);
            Add("gm.setLevel", 30);
            Add("prestige");
            Feed(800);
            Add("snack");
            Add("pat");
            return script;
        }

        public static long Local(int y, int mo, int d, int h, int mi, int offsetMinutes)
        {
            return new DateTimeOffset(y, mo, d, h, mi, 0, TimeSpan.FromMinutes(offsetMinutes)).ToUnixTimeMilliseconds();
        }

        /// <summary>一份 v1（单宠物）存档，夹了几处坏数据。</summary>
        static string V1Save(long savedAt)
        {
            return new JObject
            {
                ["v"] = 1,
                ["savedAt"] = savedAt,
                ["pet"] = new JObject { ["hunger"] = 95, ["mood"] = 12.7, ["energy"] = "x", ["exp"] = 99999, ["level"] = 4, ["form"] = "constructor", ["pride"] = 75 },
                ["totalFeeds"] = 321,
                ["totalTokens"] = 98000,
                ["tokensBySource"] = new JObject { ["user_input"] = 1000, ["generation"] = 90000, ["tool_result"] = -5 },
                ["snacks"] = 9,
                ["achievements"] = new JArray("first_feed", "first_feed", "bogus", "gourmet"),
                ["daily"] = new JObject { ["day"] = 1, ["feeds"] = 3 },
                ["streakDay"] = (savedAt + 8 * 3600000) / 86400000,
                ["streakCount"] = 2,
                ["pats"] = 48,
                ["lastFeedAt"] = savedAt - 1000,
                ["skills"] = new JObject { ["coding"] = new JObject { ["xp"] = 5000, ["level"] = 3 }, ["debug"] = "bad" },
                ["memory"] = new JObject
                {
                    ["files"] = new JArray(new JObject { ["name"] = "C:\\src\\store.js", ["count"] = 7 }, new JObject { ["name"] = "/x/store.js", ["count"] = 2 }, "bad", new JObject { ["name"] = "views.js", ["count"] = 0 }),
                    ["tools"] = new JArray(new JObject { ["name"] = "edit", ["count"] = 9 }),
                    ["hours"] = new JArray(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22),
                    ["bornDay"] = 0,
                    ["errors"] = 4,
                    ["recoveries"] = 1,
                },
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        /// <summary>一份 v2（多宠物）存档：三只宠物、收藏、蛋，夹着坏条目。</summary>
        static string V2Save(long savedAt)
        {
            JObject Pet(string id, string species, string name, long bornAt, int level, string form)
            {
                return new JObject
                {
                    ["id"] = id, ["species"] = species, ["name"] = name, ["avatar"] = species, ["icon"] = "🐾", ["bornAt"] = bornAt,
                    ["pet"] = new JObject { ["hunger"] = 40, ["mood"] = 70, ["energy"] = 20, ["exp"] = 10, ["level"] = level, ["form"] = form, ["curiosity"] = 69 },
                    ["totalFeeds"] = 1999, ["totalTokens"] = 4999000,
                    ["skills"] = new JObject { ["writing"] = new JObject { ["xp"] = 1, ["level"] = 9, ["mastery"] = 190 } },
                    ["formsOwned"] = new JArray("cat", "fox", "zzz"), ["prestige"] = 1, ["achievements"] = new JArray("first_feed"),
                };
            }
            return new JObject
            {
                ["v"] = 2,
                ["savedAt"] = savedAt,
                ["activePetId"] = "pet-b",
                ["favorites"] = new JArray("pet-c", "nobody", "pet-c"),
                ["pets"] = new JObject
                {
                    ["pet-a"] = Pet("pet-a", "whale", "大肥鱼", savedAt - 900000000, 9, ""),
                    ["pet-b"] = Pet("pet-b", "cat", "喵喵喵喵喵喵喵喵喵喵喵喵喵喵喵喵喵喵喵喵喵喵", savedAt - 500000000, 29, "cat"),
                    ["pet-c"] = Pet("pet-c", "unicorn", "", savedAt - 700000000, 3, ""),
                    ["broken"] = "nope",
                },
                ["eggs"] = new JArray(new JObject { ["id"] = "egg-1", ["type"] = "rainbow", ["obtainedAt"] = savedAt - 5 }, new JObject { ["id"] = "egg-2", ["type"] = "fake" }),
                ["global"] = new JObject { ["totalTokensAllTime"] = 4999000, ["totalFeedsAllTime"] = 1999, ["achievementsUnlockedAllTime"] = 4, ["petsHatched"] = 2, ["eggsObtained"] = new JArray("tok_50k", 3) },
            }.ToString(Newtonsoft.Json.Formatting.None);
        }

        public static TraceScript Random(string name, uint seed, int steps, int tzOffsetMinutes, long start, JObject overrides = null, string initialSave = null)
        {
            var rng = new Random((int)seed);
            var script = new TraceScript
            {
                Name = name, Seed = seed, TzOffsetMinutes = tzOffsetMinutes, Overrides = overrides, InitialSave = initialSave, BootAt = start,
            };
            var now = start;
            var lastTick = start;
            var callSeq = 0;
            var open = new List<string>();

            void Add(string op, JToken a = null, JToken b = null)
            {
                script.Steps.Add(new TraceStep { Now = now, Op = op, A = a, B = b });
            }

            void Wait(long ms)
            {
                // 空闲期间展示层每 10 秒 tick 一次；长空闲只抽几拍（结算是惰性的，中间少几拍不改变结果的形状）。
                var target = now + ms;
                var ticks = 0;
                while (lastTick + 10000 <= target && ticks < 4)
                {
                    lastTick += 10000 + rng.Next(0, 3) * 1000;
                    if (lastTick > target) break;
                    now = lastTick;
                    Add("tick");
                    ticks++;
                }
                now = target;
                if (now - lastTick > 10000)
                {
                    lastTick = now;
                    Add("tick");
                }
            }

            JObject Event(string kind)
            {
                return new JObject { ["kind"] = kind };
            }

            while (script.Steps.Count < steps)
            {
                var roll = rng.NextDouble();
                if (roll < 0.55)
                {
                    // 一轮 agent 活动。
                    var input = Event("user_input");
                    input["tokens"] = rng.Next(1, 400);
                    Add("event", input);
                    var calls = rng.Next(0, 14);
                    for (var i = 0; i < calls && script.Steps.Count < steps; i++)
                    {
                        Wait(rng.Next(100, 2500));
                        if (rng.NextDouble() < 0.35)
                        {
                            var gen = Event("generation");
                            gen["tokens"] = rng.Next(50, 80000);
                            gen["output"] = rng.Next(0, 9000);
                            Add("event", gen);
                            Wait(rng.Next(50, 800));
                        }
                        var tool = Tools[rng.Next(Tools.Length)];
                        var callId = rng.NextDouble() < 0.05 ? "" : "c" + (++callSeq);
                        var call = Event("tool_call");
                        call["tool"] = tool;
                        call["callId"] = callId;
                        if (tool == "read" || tool == "edit" || tool == "write" || rng.NextDouble() < 0.1) call["file"] = Files[rng.Next(Files.Length)];
                        Add("event", call);
                        if (callId != "") open.Add(callId);
                        Wait(rng.Next(20, 4000));
                        var result = Event("tool_result");
                        // 偶尔是找不到调用的结果（Codex 中途接上）或回头补一个旧的。
                        var r = rng.NextDouble();
                        result["callId"] = r < 0.05 ? "orphan" + rng.Next(100) : open.Count > 0 ? open[open.Count - 1] : "";
                        if (open.Count > 0 && r >= 0.05) open.RemoveAt(open.Count - 1);
                        result["tokens"] = rng.NextDouble() < 0.1 ? rng.Next(5000, 200000) : rng.Next(1, 3000);
                        result["failed"] = rng.NextDouble() < (tool == "bash" ? 0.35 : 0.12);
                        Add("event", result);
                    }
                    if (rng.NextDouble() < 0.8)
                    {
                        Wait(rng.Next(100, 3000));
                        var gen = Event("generation");
                        gen["tokens"] = rng.Next(100, 120000);
                        gen["output"] = rng.Next(0, 6000);
                        Add("event", gen);
                    }
                }
                else if (roll < 0.62)
                {
                    // 有时连着摸几下（换一套更黏人的台词），有时点得太快撞上冷却。
                    var pats = rng.NextDouble() < 0.4 ? rng.Next(2, 7) : 1;
                    for (var i = 0; i < pats; i++)
                    {
                        if (i > 0) Wait(rng.Next(800, 4000));
                        Add("pat");
                    }
                }
                else if (roll < 0.66) Add("snack");
                else if (roll < 0.69)
                {
                    Add("minigame.start");
                    var hits = rng.Next(0, 5);
                    for (var i = 0; i < hits; i++)
                    {
                        Wait(rng.Next(200, 2600));
                        Add("minigame.hit");
                    }
                    if (rng.NextDouble() < 0.3)
                    {
                        Wait(rng.Next(200, 1000));
                        Add("minigame.fail");
                    }
                }
                else if (roll < 0.70) Add("gm.grantEggs");
                else if (roll < 0.715) Add("hatchFirst", rng.NextDouble() < 0.5 ? (JToken)("小" + rng.Next(100)) : JValue.CreateNull());
                else if (roll < 0.725) Add("addPet", Species[rng.Next(Species.Length)], rng.NextDouble() < 0.5 ? (JToken)"" : "阿" + rng.Next(10));
                else if (roll < 0.745) Add("switchPet", rng.NextDouble() < 0.5 ? 1 : -1);
                else if (roll < 0.75) Add("toggleFavorite");
                else if (roll < 0.755) Add("renamePet", rng.NextDouble() < 0.2 ? "   " : "  新名字" + rng.Next(1000) + "  ");
                else if (roll < 0.765)
                {
                    var level = new[] { 2, 5, 9, 14, 19, 24, 29, 30 }[rng.Next(8)];
                    Add("gm.setLevel", level);
                    // 停在差一点就升级的地方：下一口饭正好跨过里程碑（Lv.15 / 20 / 25）。
                    if (rng.NextDouble() < 0.6) Add("gm.addExp", level * 100 - rng.Next(1, 12));
                }
                else if (roll < 0.775) Add("prestige");
                else if (roll < 0.78) Add("switchForm", Forms[rng.Next(Forms.Length)]);
                else if (roll < 0.79) Add("gm.addExp", rng.Next(0, 3000));
                else if (roll < 0.80) Add("gm.setHunger", new[] { 0, 79, 85, 100 }[rng.Next(4)]);
                else if (roll < 0.81) Add("gm.setMood", new[] { 5, 29, 31, 95 }[rng.Next(4)]);
                else if (roll < 0.815) Add("gm.setEnergy", new[] { 10, 24, 26, 100 }[rng.Next(4)]);
                else if (roll < 0.82) Add("gm.fillSnacks");
                else if (roll < 0.825) Add("gm.simulateFeed", rng.Next(0, 5000));
                else if (roll < 0.835) Add("reboot");

                // 两段活动之间的空闲。
                var gap = rng.NextDouble();
                if (gap < 0.70) Wait(rng.Next(500, 60000));
                else if (gap < 0.90) Wait(rng.Next(60000, 20 * 60000));
                else if (gap < 0.98) Wait(rng.Next(3600000, 6 * 3600000));
                else
                {
                    var away = rng.Next(10 * 3600000, 30 * 3600000);
                    if (rng.NextDouble() < 0.6)
                    {
                        // 关掉桌宠、过很久再打开：离线结算、离线自理、「好久不见」。
                        Add("reboot", away);
                        now += away;
                        lastTick = now;
                    }
                    else Wait(away);
                }
                if (rng.NextDouble() < 0.05) Add("advance");
            }
            return script;
        }
    }
}
