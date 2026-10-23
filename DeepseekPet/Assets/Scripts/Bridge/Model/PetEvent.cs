using Newtonsoft.Json;

namespace DeepseekPet.Bridge
{
    /// <summary>事件来自哪家 agent。</summary>
    public static class PetSource
    {
        public const string ClaudeCode = "claude-code";
        public const string Codex = "codex";
        public const string Dsh = "dsh";
    }

    /// <summary>
    /// 统一事件的种类。只有 user_input / generation / tool_result 三种喂食；
    /// tool_call 只长技能和记忆（和 dsh 插件 classify / observeToolCall 的分工一致）。
    /// </summary>
    public static class PetEventKind
    {
        public const string UserInput = "user_input";
        public const string Generation = "generation";
        public const string ToolCall = "tool_call";
        public const string ToolResult = "tool_result";
        public const string TurnStart = "turn_start";
        public const string TurnEnd = "turn_end";
        public const string SessionStart = "session_start";
        public const string SessionEnd = "session_end";

        /// <summary>这种事件会不会喂食。</summary>
        public static bool Feeds(string kind)
        {
            return kind == UserInput || kind == Generation || kind == ToolResult;
        }
    }

    /// <summary>
    /// 三家 agent 归一之后的事件（v1）。dsh 转发插件直接 POST 这个格式到 /event，
    /// Claude Code / Codex 由 Unity 里的适配器转出来。
    /// </summary>
    public sealed class PetEvent
    {
        [JsonProperty("v")] public int V = 1;

        /// <summary>全局唯一：`来源:会话:各家自己的唯一键`。闸门靠它去重。</summary>
        [JsonProperty("id")] public string Id = "";

        [JsonProperty("source")] public string Source = "";
        [JsonProperty("session")] public string Session = "";
        [JsonProperty("kind")] public string Kind = "";

        /// <summary>事件发生的时刻（epoch 毫秒）。闸门按它判新鲜度。</summary>
        [JsonProperty("ts")] public long Ts;

        /// <summary>喂食量的依据（换算见 TokenMath）。不喂食的事件是 0。</summary>
        [JsonProperty("tokens")] public long Tokens;

        /// <summary>只有 generation 有：模型写了多少（给「表达」技能用）。</summary>
        [JsonProperty("output")] public long Output;

        /// <summary>归一后的工具名（dsh 的小写名，见 ToolNames）。</summary>
        [JsonProperty("tool", NullValueHandling = NullValueHandling.Ignore)] public string Tool;

        /// <summary>原始工具名，只用来显示和排查。</summary>
        [JsonProperty("rawTool", NullValueHandling = NullValueHandling.Ignore)] public string RawTool;

        [JsonProperty("callId", NullValueHandling = NullValueHandling.Ignore)] public string CallId;

        /// <summary>动到的文件名（只留 basename）。</summary>
        [JsonProperty("file", NullValueHandling = NullValueHandling.Ignore)] public string File;

        [JsonProperty("failed")] public bool Failed;

        public override string ToString()
        {
            return Source + " " + Kind
                + (Tokens > 0 ? " tokens=" + Tokens : "")
                + (Tool != null ? " tool=" + Tool : "")
                + (File != null ? " file=" + File : "")
                + (Failed ? " FAILED" : "");
        }
    }
}
