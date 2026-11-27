using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

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
                var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                    scenes=EditorBuildSettings.scenes.Where(scene=>scene.enabled).Select(scene=>scene.path).ToArray(),
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
}
