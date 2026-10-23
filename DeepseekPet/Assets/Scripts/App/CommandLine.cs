using System;
using System.IO;
using System.Text;
using DeepseekPet.Bridge;
using UnityEngine;

namespace DeepseekPet.App
{
    /// <summary>
    /// 一次性命令：带参数启动时做完就退出，不起桥、不起宠物、不弹窗（配合 -batchmode -nographics）。
    /// 给 zip 里的「连接AI助手」脚本用 —— 和右键菜单「连接 Claude Code」是同一个安装器（先备份 settings.json）。
    ///
    ///   DeepseekPet.exe -batchmode -nographics --claude-status    [--result 文件]
    ///   DeepseekPet.exe -batchmode -nographics --connect-claude    [--result 文件]
    ///   DeepseekPet.exe -batchmode -nographics --disconnect-claude [--result 文件]
    ///
    /// 结果写进 --result 指定的文件（UTF-8，第一行 ok / connected / disconnected / error，第二行是说明），
    /// 退出码 0 = 成功、1 = 出错。批处理模式的播放器不往控制台打字，所以靠文件回话。
    /// </summary>
    public static class CommandLine
    {
        /// <summary>这次启动是在跑一次性命令：BridgeHost / PetHost 都别起来。</summary>
        public static bool Handled { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string command = null;
            foreach (var a in args)
            {
                if (a == "--connect-claude" || a == "--disconnect-claude" || a == "--claude-status") command = a;
            }
            if (command == null) return;
            Handled = true;

            var resultPath = ValueOf(args, "--result") ?? Path.Combine(BridgeConfig.HomeDir(), "command-result.txt");
            string status;
            string message;
            var exitCode = 0;
            try
            {
                var settings = ClaudeHookInstaller.DefaultSettingsPath();
                var current = File.Exists(settings) ? File.ReadAllText(settings) : null;
                var installed = current != null && ClaudeHookInstaller.IsInstalled(current);
                switch (command)
                {
                    case "--connect-claude":
                    {
                        var backup = ClaudeHookInstaller.Install(BridgeConfig.LoadOrCreate(BridgeConfig.DefaultPath()));
                        status = "connected";
                        message = "已连接：" + settings + (backup != null ? "（原文件备份在 " + backup + "）" : "") + "。新开的 Claude Code 会话生效。";
                        break;
                    }
                    case "--disconnect-claude":
                    {
                        if (!installed)
                        {
                            status = "disconnected";
                            message = "本来就没连接（" + settings + " 里没有桌宠的钩子）。";
                            break;
                        }
                        var backup = ClaudeHookInstaller.Uninstall();
                        status = "disconnected";
                        message = "已断开：" + settings + (backup != null ? "（原文件备份在 " + backup + "）" : "") + "。";
                        break;
                    }
                    default:
                        status = installed ? "connected" : "disconnected";
                        message = (installed ? "已连接：" : "未连接：") + settings;
                        break;
                }
            }
            catch (Exception error)
            {
                status = "error";
                message = error.Message;
                exitCode = 1;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultPath)));
                File.WriteAllText(resultPath, status + "\n" + message + "\n", new UTF8Encoding(false));
            }
            catch (Exception)
            {
                exitCode = 1;
            }
            Application.Quit(exitCode);
        }

        static string ValueOf(string[] args, string name)
        {
            var at = Array.IndexOf(args, name);
            return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
        }
    }
}
