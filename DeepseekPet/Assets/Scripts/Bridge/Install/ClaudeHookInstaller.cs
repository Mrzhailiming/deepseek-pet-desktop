using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DeepseekPet.Bridge
{
    /// <summary>
    /// 把桌宠的 http 钩子装进 / 卸出 Claude Code 的 settings.json（默认 ~/.claude/settings.json，对所有项目生效）。
    ///
    /// 只动自己的条目：凡是 url 指向 `http://127.0.0.1:*/hook/claude-code` 的钩子都算我们的，
    /// 安装 = 先删掉旧的再加一份新的（换端口 / 换口令后重装也干净），卸载 = 只删这些。
    /// 别的钩子、别的设置原样保留。每次写之前备份成 settings.json.bak-时间戳；
    /// 原文件解析不了就直接报错，不覆盖。
    ///
    /// 没有 SessionStart：Claude Code 不给 SessionStart / Setup 跑 http 钩子（会跳过并记日志）。
    /// http 钩子默认失败不阻塞（onFailure 不写），桌宠没开时 agent 照常干活。
    /// </summary>
    public static class ClaudeHookInstaller
    {
        public const string HookPath = "/hook/claude-code";

        /// <summary>要挂的事件。true = 工具事件（需要 matcher）。</summary>
        public static readonly (string Name, bool ToolEvent)[] Events =
        {
            ("UserPromptSubmit", false),
            ("PreToolUse", true),
            ("PostToolUse", true),
            ("PostToolUseFailure", true),
            ("Stop", false),
            ("SubagentStop", false),
            ("SessionEnd", false),
        };

        /// <summary>钩子请求的超时（秒）。桥收到就回 204，正常几毫秒；桌宠卡住也最多拖 agent 这么久。</summary>
        public const int TimeoutSeconds = 2;

        public static string DefaultSettingsPath()
        {
            var dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if (string.IsNullOrEmpty(dir)) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            return Path.Combine(dir, "settings.json");
        }

        /// <summary>算出装好之后的 settings.json 全文（不写盘）。</summary>
        public static string PreviewInstall(string currentJson, BridgeConfig config)
        {
            var root = ParseSettings(currentJson);
            RemoveOurs(root);
            var hooks = root["hooks"] as JObject;
            if (hooks == null)
            {
                hooks = new JObject();
                root["hooks"] = hooks;
            }
            foreach (var (name, toolEvent) in Events)
            {
                if (!(hooks[name] is JArray groups))
                {
                    groups = new JArray();
                    hooks[name] = groups;
                }
                var group = new JObject();
                if (toolEvent) group["matcher"] = "*";
                group["hooks"] = new JArray(HookEntry(config));
                groups.Add(group);
            }
            return root.ToString(Formatting.Indented);
        }

        /// <summary>算出卸载之后的 settings.json 全文（不写盘）。</summary>
        public static string PreviewUninstall(string currentJson)
        {
            var root = ParseSettings(currentJson);
            RemoveOurs(root);
            return root.ToString(Formatting.Indented);
        }

        /// <summary>现在装了没有（任意一个事件上有我们的钩子就算）。</summary>
        public static bool IsInstalled(string currentJson)
        {
            try
            {
                var root = ParseSettings(currentJson);
                return root["hooks"] is JObject hooks
                    && hooks.Properties().Any(p => p.Value is JArray groups && groups.OfType<JObject>().Any(g => g["hooks"] is JArray list && list.Any(IsOurs)));
            }
            catch (InvalidDataException)
            {
                return false;
            }
        }

        /// <summary>安装并写盘。返回备份文件路径（原来没有 settings.json 时是 null）。</summary>
        public static string Install(BridgeConfig config, string settingsPath = null)
        {
            settingsPath = settingsPath ?? DefaultSettingsPath();
            var current = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;
            return Write(settingsPath, current, PreviewInstall(current, config));
        }

        /// <summary>卸载并写盘。没装过就什么都不做，返回 null。</summary>
        public static string Uninstall(string settingsPath = null)
        {
            settingsPath = settingsPath ?? DefaultSettingsPath();
            if (!File.Exists(settingsPath)) return null;
            var current = File.ReadAllText(settingsPath);
            if (!IsInstalled(current)) return null;
            return Write(settingsPath, current, PreviewUninstall(current));
        }

        static JObject HookEntry(BridgeConfig config)
        {
            return new JObject
            {
                ["type"] = "http",
                ["url"] = "http://127.0.0.1:" + config.Port + HookPath,
                ["timeout"] = TimeoutSeconds,
                ["headers"] = new JObject { ["X-Pet-Token"] = config.Token },
            };
        }

        static bool IsOurs(JToken hook)
        {
            if (!(hook is JObject h) || h.Value<string>("type") != "http") return false;
            var url = h.Value<string>("url") ?? "";
            return url.StartsWith("http://127.0.0.1:", StringComparison.Ordinal) && url.EndsWith(HookPath, StringComparison.Ordinal);
        }

        /// <summary>删掉我们的钩子；删空的分组 / 事件 / hooks 节点一并去掉（只去掉被我们删空的）。</summary>
        static void RemoveOurs(JObject root)
        {
            if (!(root["hooks"] is JObject hooks)) return;
            foreach (var prop in hooks.Properties().ToList())
            {
                if (!(prop.Value is JArray groups)) continue;
                var touched = false;
                foreach (var group in groups.OfType<JObject>().ToList())
                {
                    if (!(group["hooks"] is JArray list)) continue;
                    var ours = list.Where(IsOurs).ToList();
                    if (ours.Count == 0) continue;
                    touched = true;
                    foreach (var hook in ours) hook.Remove();
                    if (list.Count == 0) group.Remove();
                }
                if (touched && groups.Count == 0) prop.Remove();
            }
            if (!hooks.HasValues) root.Remove("hooks");
        }

        static JObject ParseSettings(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new JObject();
            try
            {
                var token = JToken.Parse(json);
                if (token is JObject obj) return obj;
            }
            catch (JsonException error)
            {
                throw new InvalidDataException("settings.json 不是合法 JSON，没敢改：" + error.Message, error);
            }
            throw new InvalidDataException("settings.json 顶层不是对象，没敢改");
        }

        static string Write(string path, string current, string next)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string backup = null;
            if (current != null)
            {
                backup = path + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.WriteAllText(backup, current);
            }
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, next);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
            return backup;
        }
    }
}
