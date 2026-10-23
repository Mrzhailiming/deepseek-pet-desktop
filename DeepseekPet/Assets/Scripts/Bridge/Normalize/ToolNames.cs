using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace DeepseekPet.Bridge
{
    /// <summary>
    /// 工具名归一：宠物的技能表（TOOL_SKILL / EDIT_TOOLS）认的是 dsh 的小写名，
    /// Claude Code 和 Codex 的名字先映射过来。认不出的原样转小写 —— 技能表里没有
    /// 的工具会落到默认技能（research），这和 dsh 里碰到 MCP 工具时一样。
    /// </summary>
    public static class ToolNames
    {
        static readonly Dictionary<string, string> ClaudeCode = new Dictionary<string, string>
        {
            { "Read", "read" },
            { "Edit", "edit" },
            { "MultiEdit", "edit" },
            { "NotebookEdit", "edit" },
            { "Write", "write" },
            { "Bash", "bash" },
            { "PowerShell", "bash" },
            { "Grep", "grep" },
            { "Glob", "glob" },
            { "WebFetch", "web_fetch" },
            { "WebSearch", "web_search" },
            { "TodoWrite", "todo_write" },
            { "Task", "subagent" },
            { "Agent", "subagent" },
            { "Skill", "skill" },
        };

        static readonly Dictionary<string, string> Codex = new Dictionary<string, string>
        {
            { "shell", "bash" },
            { "exec_command", "bash" },
            { "local_shell", "bash" },
            { "write_stdin", "bash" },
            { "apply_patch", "edit" },
            { "update_plan", "todo_write" },
            { "view_image", "read" },
            { "web_search", "web_search" },
        };

        /// <summary>某个来源的原始工具名 → dsh 小写名。</summary>
        public static string Canonical(string source, string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            Dictionary<string, string> table = null;
            if (source == PetSource.ClaudeCode) table = ClaudeCode;
            else if (source == PetSource.Codex) table = Codex;
            if (table != null && table.TryGetValue(raw, out var mapped)) return mapped;
            return raw.ToLowerInvariant();
        }

        /// <summary>
        /// 路径 → 文件名，截到 40 字（照抄 logic.js 的 baseNameOf：记忆里只留 basename）。
        /// </summary>
        public static string BaseNameOf(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            var parts = path.Split('\\', '/');
            for (var i = parts.Length - 1; i >= 0; i--)
            {
                if (parts[i] != "") return parts[i].Length > 40 ? parts[i].Substring(0, 40) : parts[i];
            }
            return null;
        }

        /// <summary>从工具参数里挖出文件：`file_path` 优先，其次 `notebook_path` / `path`。</summary>
        public static string FileArgOf(JToken args)
        {
            if (!(args is JObject obj)) return null;
            foreach (var key in new[] { "file_path", "notebook_path", "path" })
            {
                var name = BaseNameOf(obj.Value<string>(key));
                if (name != null) return name;
            }
            return null;
        }

        /// <summary>同上，但参数是 JSON 字符串（Codex 的 arguments、dsh 的 tool/call）。</summary>
        public static string FileArgOf(string rawJson)
        {
            if (string.IsNullOrEmpty(rawJson)) return null;
            try
            {
                return FileArgOf(JToken.Parse(rawJson));
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Codex 的 apply_patch：参数是一段补丁文本，取第一个
        /// `*** Update File: <path>` / `*** Add File: <path>` 头里的文件。
        /// </summary>
        public static string FileOfPatch(string patch)
        {
            if (string.IsNullOrEmpty(patch)) return null;
            foreach (var line in patch.Split('\n'))
            {
                var trimmed = line.Trim();
                foreach (var head in new[] { "*** Update File:", "*** Add File:", "*** Delete File:" })
                {
                    if (trimmed.StartsWith(head)) return BaseNameOf(trimmed.Substring(head.Length).Trim());
                }
            }
            return null;
        }
    }
}
