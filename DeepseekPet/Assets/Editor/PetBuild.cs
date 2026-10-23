using System.IO;
using DeepseekPet.Presentation;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeepseekPet.EditorTools
{
    /// <summary>
    /// 打 Windows 包：Build/DeepseekPet.exe。菜单 Tools / 桌宠 / 打包 Windows，或命令行：
    ///   Unity.exe -batchmode -quit -projectPath DeepseekPet -executeMethod DeepseekPet.EditorTools.PetBuild.BuildWindows
    ///
    /// 顺手把透明桌宠窗口要的 Player 设置钉死（手动改过也会被改回来）：
    ///   - 关掉 DXGI flip model：flip 模式的交换链不认 DWM 的「把边框扩进客户区」，背景会变成黑的；
    ///   - 只用 D3D11；窗口化、不可缩放、不能切全屏；失焦也照常跑。
    /// 场景是空的：桥、宠物、画面都在 RuntimeInitializeOnLoadMethod 里自己建出来。
    /// </summary>
    public static class PetBuild
    {
        const string ScenePath = "Assets/Scenes/Main.unity";

        [MenuItem("Tools/桌宠/打包 Windows")]
        public static void BuildWindowsMenu()
        {
            var report = Build();
            EditorUtility.DisplayDialog("打包", report.summary.result == BuildResult.Succeeded ? "已打包到 " + report.summary.outputPath : "打包失败：" + report.summary.result, "好");
        }

        /// <summary>命令行入口：失败时以非 0 退出。</summary>
        public static void BuildWindows()
        {
            var report = Build();
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>立绘若是在 PetSpriteImporter 之前导入的，设置是旧的：重新导入一遍。</summary>
        static void EnsureSpriteImport()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Characters", "Assets/Resources/Emoji" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                // 立绘还要可读（按 alpha 判断点没点在角色身上）；emoji 不用。
                var needsReadable = path.StartsWith("Assets/Characters/", System.StringComparison.Ordinal);
                if (AssetImporter.GetAtPath(path) is TextureImporter importer
                    && (importer.textureType != TextureImporterType.Sprite || (needsReadable && !importer.isReadable)))
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }
        }

        static BuildReport Build()
        {
            EnsureSpriteImport();
            EnsureScene();
            ApplyPlayerSettings();
            var output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "Build", "DeepseekPet.exe"));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log("[PetBuild] " + report.summary.result + " → " + output + "（" + report.summary.totalSize / 1024 / 1024 + " MB，" + report.summary.totalErrors + " 个错误）");
            return report;
        }

        /// <summary>仓库根的 VERSION 文件（打包脚本给 zip 起名也读它）。</summary>
        public static string ReadVersion()
        {
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VERSION"));
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "0.0.0";
        }

        static void EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "deepseek-pet";
            PlayerSettings.productName = "DeepseekPet";
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = PetPresenter.WindowWidth;
            PlayerSettings.defaultScreenHeight = PetPresenter.WindowHeight;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.resizableWindow = false;
            PlayerSettings.allowFullscreenSwitch = false;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            // 不用 Unity 的单实例：它会把第二次启动直接转给已经开着的那份，带参数的一次性命令（CommandLine）就跑不成了。
            // 两份桌宠抢存档的问题由 BridgeHost 的命名 Mutex 挡住，第二份自己退出。
            PlayerSettings.forceSingleInstance = false;
            PlayerSettings.bundleVersion = ReadVersion();
            PlayerSettings.useFlipModelSwapchain = false;
            PlayerSettings.usePlayerLog = true;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
            // 程序图标（也是托盘图标）：🐳。
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Emoji/1f433.png");
            if (icon != null) PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown, new[] { icon });
        }
    }
}
