using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;

namespace AnimeCS.Editor
{
    public static class DesktopClientBuild
    {
        [MenuItem("AnimeCS/构建/Windows 客户端")]
        public static void BuildWindows() => Build("Builds/Windows", Array.Empty<string>());
        public static void BuildNetworkQA() => Build("Builds/QAClient", new[] { "ANIMECS_RENDER_PROBE" });

        private static void Build(string directory, string[] defines)
        {
            Directory.CreateDirectory(directory);
            string receipt=Path.Combine(directory,"build-result.txt");
            File.WriteAllText(receipt,"Building");
            try
            {
                var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
                // lilToon's build callback restores the active scene after it scans the player scenes.
                if (UnityEngine.Application.isBatchMode && scenes.Length > 0 &&
                    string.IsNullOrEmpty(EditorSceneManager.GetActiveScene().path))
                    EditorSceneManager.OpenScene(scenes[0]);
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes=scenes,
                    locationPathName=Path.Combine(directory,"AnimeCS.exe"),
                    target=BuildTarget.StandaloneWindows64, subtarget=(int)StandaloneBuildSubtarget.Player,
                    extraScriptingDefines=defines, options=BuildOptions.DetailedBuildReport
                });
                var result=report.summary;
                var errors=report.steps.SelectMany(step=>step.messages).Where(m=>m.type==UnityEngine.LogType.Error ||
                    m.type==UnityEngine.LogType.Exception).Select(m=>m.content);
                File.WriteAllText(receipt,$"{result.result}   {result.totalTime}   {result.totalSize} bytes   {result.totalErrors} errors\n"
                    +string.Join("\n",errors));
                if (result.result!=BuildResult.Succeeded) throw new BuildFailedException("Windows build "+result.result);
            }
            catch(Exception e)
            {
                File.AppendAllText(receipt,"\n"+e);
                throw;
            }
        }
    }

    // Also runs for the editor's Build And Run button, which can target any directory.
    internal sealed class WindowsLocalServicePackaging : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneWindows64 ||
                report.summary.GetSubtarget<StandaloneBuildSubtarget>() != StandaloneBuildSubtarget.Player) return;
            string source = Path.GetFullPath("Services");
            if (!Directory.Exists(source)) source = Path.GetFullPath("Builds/Windows/Services");
            string destination = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report.summary.outputPath)), "Services");
            foreach (var role in new[] { "Data", "HotUpdate", "Battle" })
            {
                string folder = Path.Combine(source, role);
                if (!File.Exists(Path.Combine(folder, $"AnimeCS.{role}.exe")))
                {
                    UnityEngine.Debug.LogWarning($"缺少本机服务构建 {role}。本地账号模式需先运行 Servers/build.ps1 -Platform Windows 生成配套服务，或配置远程服务地址。");
                    continue;
                }
                string target = Path.Combine(destination, role);
                if (!string.Equals(folder, target, StringComparison.OrdinalIgnoreCase))
                    CopyRuntime(folder, target);
            }
        }

        private static void CopyRuntime(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in new DirectoryInfo(source).EnumerateFiles())
            {
                string ext = file.Extension.ToLowerInvariant();
                if (ext == ".key" || ext == ".pid" || ext == ".log" ||
                    file.Name.IndexOf(".db", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                var output = new FileInfo(Path.Combine(target, file.Name));
                if (output.Exists && output.Length == file.Length && output.LastWriteTimeUtc == file.LastWriteTimeUtc) continue;
                file.CopyTo(output.FullName, true);
            }
            foreach (var child in new DirectoryInfo(source).EnumerateDirectories())
            {
                if (child.Name.Equals("state", StringComparison.OrdinalIgnoreCase) ||
                    child.Name.Equals("content", StringComparison.OrdinalIgnoreCase) ||
                    child.Name.EndsWith("DoNotShip", StringComparison.OrdinalIgnoreCase) ||
                    child.Name.EndsWith("DontShipItWithYourGame", StringComparison.OrdinalIgnoreCase)) continue;
                CopyRuntime(child.FullName, Path.Combine(target, child.Name));
            }
        }
    }
}
