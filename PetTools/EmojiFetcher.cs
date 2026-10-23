using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using Svg.Skia;

namespace DeepseekPet.PetTools
{
    /// <summary>
    /// fetch-emoji：给桌宠的 UI 准备彩色 emoji 图（Unity 的旧版文字只画得出单色字形）。
    ///
    /// 图来自 Twemoji（图形以 CC-BY 4.0 授权，出处登记在仓库根的 THIRD_PARTY.md）：从 npm 拿 @twemoji/svg 的包
    /// （校验 sha512），只取出桌宠实际用到的那些，栅格成 96×96 的 PNG，写到 Assets/Resources/Emoji/{码点}.png。
    /// 「用到的」= pet-data.json 里所有图标字段 + 代码里直接写的几个（见 <see cref="InCode"/>）。
    /// </summary>
    static class EmojiFetcher
    {
        const string Package = "@twemoji/svg";
        const string Version = "15.0.0";
        const string Registry = "https://registry.npmmirror.com/";
        const int Size = 96;

        /// <summary>代码里直接写的图标（PetCore / 表现层）。改了代码里的图标记得加到这里。</summary>
        static readonly string[] InCode = { "⭐", "🎮", "🔄", "🥚", "🍬", "💗", "⚡", "🍖", "🍽", "💬", "🛠", "👋", "🔁", "📋", "🔒", "✅", "🔌" };

        public static int Run(string unityDir)
        {
            var dataPath = Path.Combine(unityDir, "DeepseekPet", "Assets", "Resources", "PetData", "pet-data.json");
            var outDir = Path.Combine(unityDir, "DeepseekPet", "Assets", "Resources", "Emoji");
            var wanted = Collect(JObject.Parse(File.ReadAllText(dataPath)));
            Console.WriteLine("要 " + wanted.Count + " 个 emoji：" + string.Join(" ", wanted));

            var svgs = LoadPackage(Path.Combine(AppContext.BaseDirectory, "twemoji-svg-" + Version + ".tgz"));
            Directory.CreateDirectory(outDir);
            var missing = new List<string>();
            var written = 0;
            foreach (var emoji in wanted)
            {
                var name = FileNameOf(emoji);
                if (!svgs.TryGetValue(name + ".svg", out var svg))
                {
                    missing.Add(emoji + "(" + name + ")");
                    continue;
                }
                Rasterize(svg, Path.Combine(outDir, name + ".png"));
                written++;
            }
            Console.WriteLine("已写入 " + written + " 张 → " + outDir);
            if (missing.Count > 0) Console.WriteLine("Twemoji 里没有：" + string.Join(" ", missing) + "（这些在 UI 里会退回文字）");
            return 0;
        }

        /// <summary>数据表里的图标字段：所有键名是 icon 的字符串，加上 foodIcon / icons 两张表里的值。</summary>
        static SortedSet<string> Collect(JObject data)
        {
            var set = new SortedSet<string>(StringComparer.Ordinal);
            void Walk(JToken token, string key)
            {
                switch (token)
                {
                    case JObject o:
                        foreach (var p in o.Properties()) Walk(p.Value, p.Name);
                        break;
                    case JArray a:
                        foreach (var t in a) Walk(t, key);
                        break;
                    case JValue v when v.Type == JTokenType.String && key == "icon":
                        set.Add(v.Value<string>());
                        break;
                }
            }
            Walk(data, null);
            foreach (var tier in ((JObject)data["foodIcon"]).Properties())
            {
                foreach (var p in ((JObject)tier.Value).Properties()) set.Add(p.Value.Value<string>());
            }
            foreach (var p in ((JObject)data["icons"]).Properties()) set.Add(p.Value.Value<string>());
            foreach (var e in InCode) set.Add(e);
            set.RemoveWhere(string.IsNullOrWhiteSpace);
            return set;
        }

        /// <summary>
        /// Twemoji 的文件名：码点小写十六进制用「-」连起来；序列里没有 ZWJ（U+200D）时去掉变体选择符 U+FE0F。
        /// Unity 侧（EmojiSprites.FileNameOf）用同一条规则找图。
        /// </summary>
        public static string FileNameOf(string emoji)
        {
            var points = new List<int>();
            for (var i = 0; i < emoji.Length; i += char.IsSurrogatePair(emoji, i) ? 2 : 1) points.Add(char.ConvertToUtf32(emoji, i));
            if (!points.Contains(0x200D)) points.RemoveAll(p => p == 0xFE0F);
            return string.Join("-", points.Select(p => p.ToString("x", CultureInfo.InvariantCulture)));
        }

        /// <summary>拿 npm 包（缓存在工具目录里），校验 sha512，读出所有 .svg。</summary>
        static Dictionary<string, string> LoadPackage(string cache)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            var meta = JObject.Parse(http.GetStringAsync(Registry + Package + "/" + Version).Result);
            var integrity = meta["dist"].Value<string>("integrity");
            var tarball = meta["dist"].Value<string>("tarball");
            if (!File.Exists(cache) || !Matches(File.ReadAllBytes(cache), integrity))
            {
                Console.WriteLine("下载 " + tarball);
                var bytes = http.GetByteArrayAsync(tarball).Result;
                if (!Matches(bytes, integrity)) throw new InvalidDataException("下载的包和登记的 sha512 对不上：" + tarball);
                File.WriteAllBytes(cache, bytes);
            }
            var svgs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var file = File.OpenRead(cache);
            using var gz = new GZipStream(file, CompressionMode.Decompress);
            using var tar = new TarReader(gz);
            for (var entry = tar.GetNextEntry(); entry != null; entry = tar.GetNextEntry())
            {
                if (entry.DataStream == null || !entry.Name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) continue;
                using var reader = new StreamReader(entry.DataStream, Encoding.UTF8, false, 4096, leaveOpen: true);
                svgs[Path.GetFileName(entry.Name)] = reader.ReadToEnd();
            }
            return svgs;
        }

        static bool Matches(byte[] bytes, string integrity)
        {
            if (integrity == null || !integrity.StartsWith("sha512-", StringComparison.Ordinal)) return false;
            return Convert.ToBase64String(SHA512.HashData(bytes)) == integrity.Substring(7);
        }

        static void Rasterize(string svgText, string path)
        {
            using var svg = new SKSvg();
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(svgText)))
            {
                if (svg.Load(stream) == null) throw new InvalidOperationException("SVG 解析失败：" + path);
            }
            var bounds = svg.Picture.CullRect;
            using var bitmap = new SKBitmap(new SKImageInfo(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);
                // Twemoji 的 viewBox 是 0 0 36 36。
                var scale = Size / Math.Max(bounds.Width, bounds.Height);
                canvas.Scale(scale);
                canvas.DrawPicture(svg.Picture);
            }
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var file = File.Create(path);
            data.SaveTo(file);
        }
    }
}
