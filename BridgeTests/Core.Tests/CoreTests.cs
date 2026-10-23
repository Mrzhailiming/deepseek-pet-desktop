using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using DeepseekPet.Replay;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace DeepseekPet.Core.Tests
{
    static class Fixtures
    {
        static PetData _data;

        public static string DataPath => Path.Combine(AppContext.BaseDirectory, "PetData", "pet-data.json");

        public static PetData Data => _data ??= PetData.Parse(File.ReadAllText(DataPath));

        public static string[] TraceNames()
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "Traces");
            return Directory.GetFiles(dir, "*.json.gz").Select(Path.GetFileName).OrderBy(n => n).ToArray();
        }

        public static JObject ReadTrace(string name)
        {
            using (var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Traces", name)))
            using (var gz = new GZipStream(file, CompressionMode.Decompress))
            using (var reader = new StreamReader(gz))
            {
                return JObject.Parse(reader.ReadToEnd());
            }
        }
    }

    /// <summary>
    /// 金样回放：原 JS（dsh-pet-plugin/lib/src/store.js）在 Jint 里跑出来的轨迹，C# 的 PetCore 必须每一步、
    /// 每个字段都一样。规则改了就重新生成：dotnet run --project PetTools -- gen-traces。
    /// </summary>
    public class TraceTests
    {
        [TestCaseSource(typeof(Fixtures), nameof(Fixtures.TraceNames))]
        public void ReplaysLikeTheJsStore(string name)
        {
            var script = TraceFile.Read(Fixtures.ReadTrace(name), out var golden);
            Assert.Greater(script.Steps.Count, 10);
            var diff = TraceCheck.Run(Fixtures.Data, script, golden);
            Assert.IsNull(diff, diff);
        }

        [Test]
        public void AllPresetsArePresent()
        {
            CollectionAssert.IsSupersetOf(Fixtures.TraceNames(), new[]
            {
                "default-cn.json.gz", "night-owl.json.gz", "utc-minus5.json.gz", "knobs-off.json.gz",
                "fast-growth.json.gz", "import-v1.json.gz", "import-v2.json.gz", "milestones.json.gz",
            });
        }
    }

    public class DataTests
    {
        [Test]
        public void ConfigDefaultsComeFromPetData()
        {
            var config = PetConfig.Create(Fixtures.Data);
            Assert.AreEqual(5000, config.ComboWindowMs);
            Assert.AreEqual(2.5, config.FoodPerDouble);
            Assert.AreEqual("tool_result", config.FavoriteSource);
            Assert.IsTrue(config.Persist);
        }

        [Test]
        public void OverridesOnlyTakeTheSameType()
        {
            var config = PetConfig.Create(Fixtures.Data, JObject.Parse("{\"maxCombo\": 6, \"maxFood\": 12.5, \"persist\": \"no\", \"foodPerDouble\": 3, \"unknown\": 1}"));
            Assert.AreEqual(6, config.MaxCombo);
            Assert.AreEqual(30, config.MaxFood, "整数字段不收小数");
            Assert.IsTrue(config.Persist, "类型不对就忽略");
            Assert.AreEqual(3.0, config.FoodPerDouble, "double 字段收整数");
        }

        [Test]
        public void EveryTableEntryHasAnImplementation()
        {
            var data = Fixtures.Data;
            Assert.IsTrue(data.Achievements.All(a => a.Test != null));
            Assert.IsTrue(data.QuestPool.All(q => q.Of != null));
            Assert.IsTrue(data.EggMilestones.All(m => m.Test != null));
            Assert.AreEqual("新手", PetTables.TitleOf(data, 1, 0, 0));
            Assert.AreEqual("全图鉴", PetTables.TitleOf(data, 30, 3, 4));
        }

        [Test]
        public void MissingImplementationFailsLoudly()
        {
            var json = JObject.Parse(File.ReadAllText(Fixtures.DataPath));
            ((JArray)json["achievements"]).Add(new JObject { ["id"] = "brand_new", ["icon"] = "x", ["label"] = "x", ["hint"] = "x" });
            var error = Assert.Throws<InvalidOperationException>(() => PetData.Parse(json.ToString()));
            StringAssert.Contains("brand_new", error.Message);
        }
    }

    /// <summary>和 JS 数值行为逐位一致的那几个坑。</summary>
    public class JsNumericTests
    {
        [TestCase(0.15, "0.1")]
        [TestCase(1.15, "1.1")]
        [TestCase(1.25, "1.3")]
        [TestCase(1.05, "1.1")]
        [TestCase(999.94, "999.9")]
        [TestCase(12.0, "12.0")]
        public void ToFixedOneMatchesJs(double x, string expected)
        {
            // 0.15 / 1.15 的 double 实际略小于 .x5，JS 舍去；1.25 是精确的 .x5，取大；1.05 实际略大。
            Assert.AreEqual(expected, PetRules.ToFixed1(x));
        }

        [TestCase(1150, "1.1k")]
        [TestCase(999, "999")]
        [TestCase(1000, "1k")]
        [TestCase(999949, "999.9k")]
        [TestCase(999950, "1M")]
        [TestCase(41745, "41.7k")]
        public void FormatTokens(long tokens, string expected)
        {
            Assert.AreEqual(expected, PetRules.FormatTokens(tokens));
        }

        [Test]
        public void Log2IsExactOnPowersOfTwo()
        {
            for (var k = 0; k < 60; k++) Assert.AreEqual((double)k, JsMath.Log2(Math.Pow(2, k)));
        }

        [Test]
        public void RoundIsHalfUpNotBankers()
        {
            Assert.AreEqual(3, JsMath.Round(2.5));
            Assert.AreEqual(-2, JsMath.Round(-2.5));
        }

        [Test]
        public void FoodCurve()
        {
            var config = PetConfig.Create(Fixtures.Data);
            // 2.5 × log2(1 + tokens/60)，四舍五入。
            Assert.AreEqual(0, PetRules.FoodFromTokens(0, config));
            Assert.AreEqual(3, PetRules.FoodFromTokens(60, config));
            Assert.AreEqual(5, PetRules.FoodFromTokens(180, config));
            Assert.AreEqual(25, PetRules.FoodFromTokens(60 * 1023, config));
        }
    }

    public class CoreBehaviourTests
    {
        static PetCore NewCore(ManualClock clock, MemoryStorage storage, string overrides = null)
        {
            var data = Fixtures.Data;
            var config = PetConfig.Create(data, overrides == null ? null : JObject.Parse(overrides));
            return new PetCore(data, config, clock, new Mulberry32(7), storage, TraceScript.ZoneOf(480));
        }

        [Test]
        public void ToolEventsFeedAndGrowSkills()
        {
            var clock = new ManualClock { Now = 1792452600000 };
            var core = NewCore(clock, new MemoryStorage());
            var changes = 0;
            core.Changed += () => changes++;
            core.HandleEvent("tool_call", 0, 0, "edit", "c1", "store.js", false);
            Assert.AreEqual(0, core.State.TotalFeeds, "工具调用不喂食");
            Assert.AreEqual("store.js", core.State.Memory.Files[0].Name);
            clock.Now += 800;
            core.HandleEvent("tool_result", 2000, 0, "edit", "c1", null, false);
            Assert.AreEqual(1, core.State.TotalFeeds);
            Assert.AreEqual(2000, core.State.TokensBySource.ToolResult);
            Assert.Less(core.State.Pet.Hunger, 60);
            Assert.AreEqual(1, core.State.Effects.Count(e => e.Source == "tool_result"));
            Assert.Greater(changes, 0);
            // 特效 2.2s 后自己撤掉（定时器在 Update 里跑）。
            clock.Now += 2300;
            core.Update();
            Assert.AreEqual(0, core.State.Effects.Count(e => e.Source == "tool_result"));
        }

        [Test]
        public void SaveRoundTripsAndOfflineTimePasses()
        {
            var clock = new ManualClock { Now = 1792452600000 };
            var storage = new MemoryStorage();
            var core = NewCore(clock, storage);
            for (var i = 0; i < 5; i++)
            {
                clock.Now += 1000;
                core.HandleEvent("user_input", 50, 0, null, null, null, false);
            }
            var level = core.State.Pet.Level;
            var exp = core.State.Pet.Exp;
            var hunger = core.State.Pet.Hunger;
            core.Dispose();
            Assert.IsNotNull(storage.Content);
            var saved = JObject.Parse(storage.Content);
            Assert.AreEqual(2, saved.Value<int>("v"));
            Assert.AreEqual(5, saved["pets"]["pet-0"].Value<int>("totalFeeds"));

            // 关掉 3 小时：饿了，进度还在。
            clock.Now += 3 * 3600000;
            var again = NewCore(clock, storage);
            Assert.AreEqual(5, again.State.TotalFeeds);
            Assert.AreEqual(level, again.State.Pet.Level);
            Assert.AreEqual(exp, again.State.Pet.Exp);
            Assert.Greater(again.State.Pet.Hunger, hunger);
        }

        [Test]
        public void GarbageSaveStartsFresh()
        {
            var clock = new ManualClock { Now = 1792452600000 };
            foreach (var raw in new[] { "{", "null", "[]", "{\"v\":3}", "{\"v\":2,\"pets\":[]}" })
            {
                var core = NewCore(clock, new MemoryStorage(raw));
                Assert.AreEqual(1, core.State.Pet.Level, raw);
                Assert.AreEqual(1, core.State.PetCount, raw);
            }
        }

        [Test]
        public void PersistOffNeverWrites()
        {
            var clock = new ManualClock { Now = 1792452600000 };
            var storage = new MemoryStorage();
            var core = NewCore(clock, storage, "{\"persist\": false}");
            core.HandleEvent("user_input", 50, 0, null, null, null, false);
            clock.Now += 10000;
            core.Update();
            core.Dispose();
            Assert.IsNull(storage.Content);
        }
    }
}
