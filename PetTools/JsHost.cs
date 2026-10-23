using System;
using System.IO;
using System.Linq;
using Jint;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DeepseekPet.PetTools
{
    /// <summary>
    /// 一份在 Jint 里跑起来的原 JS store。harness.js 提供假时钟 / 定时器 / 随机数 / localStorage，
    /// 然后把 config / art / constants / logic / store 五个源文件和 api.js 拼进同一个函数作用域执行。
    /// </summary>
    sealed class JsHost
    {
        static readonly string[] Sources = { "config.js", "art.js", "constants.js", "logic.js", "store.js" };

        readonly Engine _engine;

        public JsHost(string srcDir, TimeZoneInfo zone, uint seed)
        {
            _engine = new Engine(o => o.LocalTimeZone(zone));
            var here = AppContext.BaseDirectory;
            _engine.Execute(File.ReadAllText(Path.Combine(here, "harness.js")));
            _engine.Execute("__seed = " + unchecked((int)seed) + ";");
            var body = string.Join("\n", Sources.Select(f => File.ReadAllText(Path.Combine(srcDir, f))));
            _engine.Execute("(function () {\n" + body + "\n" + File.ReadAllText(Path.Combine(here, "api.js")) + "\n})();");
        }

        /// <summary>仓库根目录（有 DeepseekPet/Assets 的那一层）：从当前目录往上找，找不到再从工具自己的目录往上找。</summary>
        public static string FindRepoRoot()
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, "DeepseekPet", "Assets"))) return dir.FullName;
                }
            }
            throw new DirectoryNotFoundException("往上找不到 DeepseekPet/Assets（请在仓库里运行）");
        }

        /// <summary>
        /// 原 JS 规格的目录：--spec 参数 &gt; 环境变量 DEEPSEEK_PET_JS_SPEC &gt; 仓库里的快照 PetTools/spec。
        /// 想对着网页版插件的最新源码跑，就传 --spec &lt;deepseek-pet&gt;/dsh-pet-plugin/lib/src。
        /// </summary>
        public static string FindSourceDir(string root, string[] args)
        {
            var at = Array.IndexOf(args, "--spec");
            var src = at >= 0 && at + 1 < args.Length ? args[at + 1]
                : Environment.GetEnvironmentVariable("DEEPSEEK_PET_JS_SPEC") ?? Path.Combine(root, "PetTools", "spec");
            if (!File.Exists(Path.Combine(src, "store.js"))) throw new DirectoryNotFoundException("规格目录里没有 store.js：" + src);
            return Path.GetFullPath(src);
        }

        /// <summary>把时间推到 now：到期的定时器按顺序跑掉。</summary>
        public void Advance(long now)
        {
            _engine.Execute("__advance(" + now + ");");
        }

        public void Boot(string overridesJson, string savedState)
        {
            _engine.Invoke(_engine.Evaluate("__pet.boot"), overridesJson == null ? (object)Jint.Native.JsValue.Null : overridesJson,
                savedState == null ? (object)Jint.Native.JsValue.Null : savedState);
        }

        public JObject Snap()
        {
            return JObject.Parse(_engine.Evaluate("JSON.stringify(__pet.snap())").AsString());
        }

        public JToken Data()
        {
            return JToken.Parse(_engine.Evaluate("JSON.stringify(__pet.data())").AsString());
        }

        public string Storage()
        {
            var v = _engine.Evaluate("__pet.storage()");
            return v.IsNull() ? null : v.AsString();
        }

        /// <summary>执行一步，返回值（bool / null）原样带回。</summary>
        public JToken Op(string op, JToken a, JToken b)
        {
            var script = "JSON.stringify(__pet.op(" + JsonConvert.ToString(op) + ", "
                + (a == null ? "undefined" : a.ToString(Formatting.None)) + ", "
                + (b == null ? "undefined" : b.ToString(Formatting.None)) + "))";
            var result = _engine.Evaluate(script);
            return result.IsUndefined() ? JValue.CreateNull() : JToken.Parse(result.AsString());
        }
    }
}
