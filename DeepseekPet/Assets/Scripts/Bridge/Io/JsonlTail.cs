using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DeepseekPet.Bridge
{
    /// <summary>
    /// 追读一个持续追加的 JSONL 文件（Claude 的 transcript、Codex 的 rollout）。
    ///
    /// - 按字节偏移读新增部分，只交出以 `\n` 结尾的完整行；尾巴上写了一半的那行留到下次。
    /// - 以 ReadWrite | Delete 共享方式打开：写的一方（agent 进程）不会因为我们在读而写失败。
    /// - 文件变短（被截断 / 换了一个同名文件）就从头读。
    /// 不是线程安全的：一个实例只在一个线程上用（或调用方自己加锁）。
    /// </summary>
    public sealed class JsonlTail
    {
        public string Path { get; }
        long _offset;
        readonly List<byte> _partial = new List<byte>();

        /// <param name="startOffset">从这个字节偏移开始读（之前的内容当作已经看过）。</param>
        public JsonlTail(string path, long startOffset)
        {
            Path = path;
            _offset = Math.Max(0, startOffset);
        }

        /// <param name="fromEnd">true：从当前末尾开始（不回放已有内容）；false：从头读。</param>
        public JsonlTail(string path, bool fromEnd)
        {
            Path = path;
            if (fromEnd)
            {
                try
                {
                    _offset = new FileInfo(path).Length;
                }
                catch (IOException)
                {
                    _offset = 0;
                }
                catch (UnauthorizedAccessException)
                {
                    _offset = 0;
                }
            }
        }

        /// <summary>读出自上次以来新增的完整行（不含换行符）。文件不存在 / 读失败时返回空表。</summary>
        public List<string> ReadNewLines()
        {
            var lines = new List<string>();
            FileStream stream;
            try
            {
                stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            }
            catch (IOException)
            {
                return lines;
            }
            catch (UnauthorizedAccessException)
            {
                return lines;
            }
            using (stream)
            {
                var length = stream.Length;
                if (length < _offset)
                {
                    _offset = 0;
                    _partial.Clear();
                }
                if (length == _offset) return lines;
                stream.Seek(_offset, SeekOrigin.Begin);
                var buffer = new byte[64 * 1024];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    _offset += read;
                    for (var i = 0; i < read; i++)
                    {
                        if (buffer[i] == (byte)'\n')
                        {
                            var line = Encoding.UTF8.GetString(_partial.ToArray()).TrimEnd('\r');
                            _partial.Clear();
                            if (line.Length > 0) lines.Add(line);
                        }
                        else
                        {
                            _partial.Add(buffer[i]);
                        }
                    }
                }
            }
            return lines;
        }
    }
}
