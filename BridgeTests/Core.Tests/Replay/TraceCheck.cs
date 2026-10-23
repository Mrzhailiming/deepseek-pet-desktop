using System.Collections.Generic;
using DeepseekPet.Core;
using Newtonsoft.Json.Linq;

namespace DeepseekPet.Replay
{
    /// <summary>原 JS 跑出来的金样：开机快照、每一步的返回值与快照、最后留在存档里的内容。</summary>
    public sealed class TraceGolden
    {
        public JObject Boot;
        public List<JToken> Rets = new List<JToken>();
        public List<JObject> Snaps = new List<JObject>();
        public string FinalSave;
    }

    /// <summary>
    /// 轨迹文件（Core.Tests/Traces/*.json）：剧本 + 金样。快照只存和上一步不同的顶层字段，读的时候叠回去。
    /// </summary>
    public static class TraceFile
    {
        public static JObject Write(TraceScript script, TraceGolden golden)
        {
            var steps = new JArray();
            JObject previous = golden.Boot;
            for (var i = 0; i < script.Steps.Count; i++)
            {
                var step = script.Steps[i];
                var item = new JObject { ["now"] = step.Now, ["op"] = step.Op };
                if (step.A != null) item["a"] = step.A;
                if (step.B != null) item["b"] = step.B;
                if (golden.Rets[i] != null && golden.Rets[i].Type != JTokenType.Null) item["ret"] = golden.Rets[i];
                item["d"] = JsonDiff.Delta(previous, golden.Snaps[i]);
                previous = golden.Snaps[i];
                steps.Add(item);
            }
            return new JObject
            {
                ["name"] = script.Name,
                ["tzOffsetMinutes"] = script.TzOffsetMinutes,
                ["seed"] = script.Seed,
                ["bootAt"] = script.BootAt,
                ["overrides"] = script.Overrides,
                ["initialSave"] = script.InitialSave,
                ["boot"] = golden.Boot,
                ["steps"] = steps,
                ["finalSave"] = golden.FinalSave,
            };
        }

        public static TraceScript Read(JObject file, out TraceGolden golden)
        {
            var script = new TraceScript
            {
                Name = file.Value<string>("name"),
                TzOffsetMinutes = file.Value<int>("tzOffsetMinutes"),
                Seed = file.Value<uint>("seed"),
                BootAt = file.Value<long>("bootAt"),
                Overrides = file["overrides"] as JObject,
                InitialSave = file["initialSave"]?.Type == JTokenType.String ? file.Value<string>("initialSave") : null,
            };
            golden = new TraceGolden
            {
                Boot = (JObject)file["boot"],
                FinalSave = file["finalSave"]?.Type == JTokenType.String ? file.Value<string>("finalSave") : null,
            };
            var previous = golden.Boot;
            foreach (JObject item in (JArray)file["steps"])
            {
                script.Steps.Add(new TraceStep { Now = item.Value<long>("now"), Op = item.Value<string>("op"), A = item["a"], B = item["b"] });
                golden.Rets.Add(item["ret"] ?? JValue.CreateNull());
                previous = JsonDiff.Apply(previous, (JObject)item["d"]);
                golden.Snaps.Add(previous);
            }
            return script;
        }
    }

    public static class TraceCheck
    {
        /// <summary>用 C# 的 PetCore 回放剧本，逐步和金样比；全对返回 null，否则返回第一处分歧的说明。</summary>
        public static string Run(PetData data, TraceScript script, TraceGolden golden)
        {
            var runner = new CoreRunner(data, script);
            var diff = JsonDiff.FirstDifference(golden.Boot, runner.Snap());
            if (diff != null) return script.Name + " 开机：" + diff;
            for (var i = 0; i < script.Steps.Count; i++)
            {
                var step = script.Steps[i];
                var ret = runner.Run(step);
                var where = script.Name + " 第 " + i + " 步（" + step.Op + (step.A != null ? " " + step.A.ToString(Newtonsoft.Json.Formatting.None) : "") + " @ " + step.Now + "）";
                diff = JsonDiff.FirstDifference(golden.Rets[i], ret, "ret");
                if (diff != null) return where + "：" + diff;
                diff = JsonDiff.FirstDifference(golden.Snaps[i], runner.Snap());
                if (diff != null) return where + "：" + diff;
            }
            if (golden.FinalSave != null || runner.Storage != null)
            {
                if (golden.FinalSave == null || runner.Storage == null) return script.Name + " 最后的存档：一边有一边没有";
                diff = JsonDiff.FirstDifference(JToken.Parse(golden.FinalSave), JToken.Parse(runner.Storage), "save");
                if (diff != null) return script.Name + " 最后的存档：" + diff;
            }
            return null;
        }
    }
}
