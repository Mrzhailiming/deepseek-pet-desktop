using System;
using System.IO;
using System.Text;

namespace DeepseekPet.Core
{
    /// <summary>
    /// 存档文件：写到同目录的 .tmp 再换上去（File.Replace，上一版留在 .bak），写到一半断电也不会把存档写坏。
    /// 主文件读不出来（被删 / 半截）时退回 .bak。
    /// </summary>
    public sealed class FileStorage : IPetStorage
    {
        readonly string _path;

        public FileStorage(string path)
        {
            _path = path;
        }

        public string Path => _path;
        public string BackupPath => _path + ".bak";

        /// <summary>~/.deepseek-pet/pet-save.json（和桥的 bridge.json 放在一起）。</summary>
        public static string DefaultPath()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return System.IO.Path.Combine(home, ".deepseek-pet", "pet-save.json");
        }

        public string Read()
        {
            var text = TryRead(_path);
            return string.IsNullOrWhiteSpace(text) ? TryRead(BackupPath) : text;
        }

        static string TryRead(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        public void Write(string json)
        {
            var dir = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            if (File.Exists(_path)) File.Replace(tmp, _path, BackupPath, true);
            else File.Move(tmp, _path);
        }
    }
}
