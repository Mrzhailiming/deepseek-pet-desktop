using System;
using System.Text;

namespace DeepseekPet.Bridge
{
    /// <summary>
    /// 喂食量换算，照抄 dsh-pet-plugin/lib/src/logic.js 的 classify()，三家来源共用：
    ///   user_input   ceil(字符数 / 2)
    ///   tool_result  max(ceil(UTF-8 字节数 / 2), 1)
    /// generation 直接用各家报的 token 数，不在这里换算。
    /// </summary>
    public static class TokenMath
    {
        /// <summary>用户输入：字符数按 UTF-16 码元算，和 JS 的 string.length 一致。</summary>
        public static long UserInputTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return (text.Length + 1) / 2;
        }

        /// <summary>工具结果：按结果文本（通常是紧凑 JSON）的 UTF-8 字节数估算，至少 1。</summary>
        public static long ToolResultTokens(string serialized)
        {
            var bytes = string.IsNullOrEmpty(serialized) ? 0 : Encoding.UTF8.GetByteCount(serialized);
            return Math.Max((bytes + 1) / 2, 1);
        }
    }
}
