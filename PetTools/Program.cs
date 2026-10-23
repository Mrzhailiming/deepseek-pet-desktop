using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using DeepseekPet.Core;
using DeepseekPet.Replay;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DeepseekPet.PetTools
{
    static class Program
    {
        const string Usage = "用法: PetTools export-data | render-art | fetch-emoji | gen-traces | fuzz [--seeds N] [--steps N] [--from N]  [--spec <原 JS 目录>]";

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            var command = args.Length > 0 ? args[0] : "";
            var unity = JsHost.FindRepoRoot();
            var src = JsHost.FindSourceDir(unity, args);
            switch (command)
            {
                case "export-data":
                {
                    var host = new JsHost(src, TimeZoneInfo.Utc, 1);
                    var path = Path.Combine(unity, "DeepseekPet", "Assets", "Resources", "PetData", "pet-data.json");
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, host.Data().ToString(Formatting.Indented) + "\n", new UTF8Encoding(false));
                    Console.WriteLine("已写入 " + path);
                    return 0;
                }
                case "fetch-emoji":
                    return EmojiFetcher.Run(unity);
                case "render-art":
                    return ArtRenderer.Run(src, Path.Combine(unity, "DeepseekPet", "Assets", "Characters", "Sprite", "Resources", "FormSprites"));
                case "gen-traces":
                {
                    var dir = Path.Combine(unity, "BridgeTests", "Core.Tests", "Traces");
                    Directory.CreateDirectory(dir);
                    var failures = 0;
                    foreach (var script in Scenarios.Presets())
                    {
                        var watch = Stopwatch.StartNew();
                        var golden = RunJs(src, script, out var data);
                        // gzip：原样 JSON 一份 1~2 MB，压完几十 KB。
                        var path = Path.Combine(dir, script.Name + ".json.gz");
                        using (var file = File.Create(path))
                        using (var gz = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionLevel.Optimal))
                        {
                            var bytes = new UTF8Encoding(false).GetBytes(TraceFile.Write(script, golden).ToString(Formatting.None));
                            gz.Write(bytes, 0, bytes.Length);
                        }
                        var diff = Compare(data, script, golden);
                        Console.WriteLine((diff == null ? "✓ " : "✗ ") + script.Name + "：" + script.Steps.Count + " 步，"
                            + (new FileInfo(path).Length / 1024) + " KiB，" + watch.ElapsedMilliseconds + " ms" + (diff == null ? "" : "\n    " + diff));
                        if (diff != null) failures++;
                    }
                    return failures == 0 ? 0 : 1;
                }
                case "fuzz":
                {
                    var seeds = IntArg(args, "--seeds", 20);
                    var steps = IntArg(args, "--steps", 1500);
                    var from = IntArg(args, "--from", 1000);
                    var failures = 0;
                    var zones = new[] { 480, 0, -300, 330, 600, -480 };
                    for (var i = 0; i < seeds; i++)
                    {
                        var seed = (uint)(from + i);
                        var tz = zones[i % zones.Length];
                        var start = Scenarios.Local(2026, 1 + i % 12, 1 + i % 27, i % 24, (i * 7) % 60, tz);
                        var script = Scenarios.Random("fuzz-" + seed, seed, steps, tz, start);
                        var golden = RunJs(src, script, out var data);
                        var diff = Compare(data, script, golden);
                        Console.WriteLine((diff == null ? "✓ " : "✗ ") + script.Name + (diff == null ? "" : "\n    " + diff));
                        if (diff != null) failures++;
                    }
                    Console.WriteLine(failures == 0 ? "全部一致" : failures + " 个剧本有分歧");
                    return failures == 0 ? 0 : 1;
                }
                default:
                    Console.Error.WriteLine(Usage);
                    return 2;
            }
        }

        static int IntArg(string[] args, string name, int fallback)
        {
            var at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length && int.TryParse(args[at + 1], out var v) ? v : fallback;
        }

        /// <summary>在 Jint 里跑一遍原 JS，得到金样（开机快照 + 每一步的返回值和快照 + 最后的存档）。</summary>
        static TraceGolden RunJs(string src, TraceScript script, out PetData data)
        {
            var host = new JsHost(src, script.Zone(), script.Seed);
            data = PetData.Parse(host.Data().ToString(Formatting.None));
            host.Advance(script.BootAt);
            host.Boot(script.Overrides?.ToString(Formatting.None), script.InitialSave);
            var golden = new TraceGolden { Boot = host.Snap() };
            foreach (var step in script.Steps)
            {
                host.Advance(step.Now);
                golden.Rets.Add(host.Op(step.Op, step.A, step.B));
                golden.Snaps.Add(host.Snap());
            }
            golden.FinalSave = host.Storage();
            return golden;
        }

        static string Compare(PetData data, TraceScript script, TraceGolden golden)
        {
            return TraceCheck.Run(data, script, golden);
        }
    }
}
