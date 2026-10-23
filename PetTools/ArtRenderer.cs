using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jint;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using Svg.Skia;

namespace DeepseekPet.PetTools
{
    /// <summary>
    /// render-art：把网页版的矢量形态（art.js 的 FORM_ART，经 views.js 的 svgAvatar 画出来）栅格化成 PNG，
    /// 给 Unity 的立绘角色用 —— 进化后的代码猫 / 探索狐 / 调试虫 / 文鸟，以及孵出来的同种族宠物。
    ///
    /// 每种形态出 6 张状态图（normal / hungry / sleep / sad / excited / pat）和 3 张情绪三维的脸
    /// （normal-pride / normal-curiosity / normal-concern），512×512，透明底。
    /// </summary>
    static class ArtRenderer
    {
        static readonly string[] Sources = { "config.js", "art.js", "constants.js", "logic.js", "views.js" };
        static readonly string[] States = { "normal", "hungry", "sleep", "sad", "excited", "pat" };
        const int Size = 512;

        public static int Run(string src, string outDir)
        {
            var engine = new Engine(o => o.LocalTimeZone(TimeZoneInfo.Utc));
            var here = AppContext.BaseDirectory;
            engine.Execute(File.ReadAllText(Path.Combine(here, "harness.js")));
            var body = string.Join("\n", Sources.Select(f => File.ReadAllText(Path.Combine(src, f))));
            engine.Execute("(function () {\nvar React = {};\n" + body + "\n" + File.ReadAllText(Path.Combine(here, "art-api.js")) + "\n})();");

            var forms = JArray.Parse(engine.Evaluate("__art.forms()").AsString()).Select(t => t.Value<string>()).ToList();
            var dims = JArray.Parse(engine.Evaluate("__art.dims()").AsString()).Select(t => t.Value<string>()).ToList();
            Directory.CreateDirectory(outDir);
            var count = 0;
            foreach (var form in forms)
            {
                var jobs = new List<(string name, string state, string dim)>();
                foreach (var state in States) jobs.Add((form + "-" + state, state, null));
                foreach (var dim in dims) jobs.Add((form + "-normal-" + dim, "normal", dim));
                foreach (var (name, state, dim) in jobs)
                {
                    // 等级给 10：进化形态的长相不随等级变，给个够格的数就行。
                    var svg = engine.Invoke(engine.Evaluate("__art.render"), 10, form, state, dim == null ? (object)Jint.Native.JsValue.Null : dim).AsString();
                    Rasterize(svg, Path.Combine(outDir, name + ".png"));
                    count++;
                }
            }
            Console.WriteLine("已渲染 " + count + " 张 → " + outDir);
            return 0;
        }

        static void Rasterize(string svgText, string path)
        {
            using var svg = new SKSvg();
            using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svgText)))
            {
                if (svg.Load(stream) == null) throw new InvalidOperationException("SVG 解析失败：" + path);
            }
            var picture = svg.Picture;
            var bounds = picture.CullRect;
            using var bitmap = new SKBitmap(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);
                // viewBox 是 0 0 64 64：整张铺满。
                canvas.Scale(Size / 64f);
                canvas.DrawPicture(picture);
            }
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(path);
            data.SaveTo(file);
        }
    }
}
