using System;
using System.IO;
using DeepseekPet.Platform;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DeepseekPet.Presentation
{
    /// <summary>
    /// 桌宠窗口的位置：记在 ~/.deepseek-pet/window.json，下次打开还在原地；
    /// 显示器拔了 / 分辨率变了就钳回最近那块屏的工作区里（至少露出大半个窗口）。
    /// </summary>
    public static class WindowPlacement
    {
        public static string FilePath()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".deepseek-pet", "window.json");
        }

        /// <summary>上次的左上角；没记过就放在主屏工作区的右下角。</summary>
        public static Vector2Int Load(int width, int height)
        {
            try
            {
                var path = FilePath();
                if (File.Exists(path))
                {
                    var json = JObject.Parse(File.ReadAllText(path));
                    var x = json.Value<int?>("x");
                    var y = json.Value<int?>("y");
                    if (x.HasValue && y.HasValue) return Clamp(x.Value, y.Value, width, height);
                }
            }
            catch (Exception error) when (error is IOException || error is Newtonsoft.Json.JsonException || error is UnauthorizedAccessException)
            {
                // 读不了就当没记过。
            }
            var work = DesktopWindow.WorkAreaAt(0, 0);
            return new Vector2Int(work.Right - width - 24, work.Bottom - height);
        }

        public static void Save(int x, int y)
        {
            try
            {
                var path = FilePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, new JObject { ["x"] = x, ["y"] = y }.ToString());
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                // 没记下来，下次回到默认位置而已。
            }
        }

        /// <summary>钳到窗口中心所在（或最近的）那块屏的工作区里。</summary>
        public static Vector2Int Clamp(int x, int y, int width, int height)
        {
            var work = DesktopWindow.WorkAreaAt(x + width / 2, y + height / 2);
            // 左右允许探出去四分之一（宠物站在窗口中间，两侧是空的），但别整个跑丢。
            // 上下一点都不许：头顶是台词气泡，底下是状态条，探出去就看不见了（实测过：上边探出三分之一会把气泡藏掉，
            // 150% 缩放下底边探出八分之一会把状态条压到任务栏后面）。
            var slackX = width / 4;
            x = Mathf.Clamp(x, work.Left - slackX, work.Right - width + slackX);
            y = Mathf.Clamp(y, work.Top, work.Bottom - height);
            return new Vector2Int(x, y);
        }
    }
}
