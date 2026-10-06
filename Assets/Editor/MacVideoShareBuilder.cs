#if UNITY_EDITOR_OSX
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace IslandAirport.Editor
{
    /// <summary>Rebuilds the tiny AppKit bundle from source when missing; no third-party dependencies.</summary>
    [InitializeOnLoad]
    public sealed class MacVideoShareBuilder : IPreprocessBuildWithReport
    {
        private const string Root = "Assets/Plugins/macOS";
        public int callbackOrder { get { return 10; } }
        static MacVideoShareBuilder()
        {
            EditorApplication.delayCall += delegate
            {
                if (!File.Exists(Root + "/PalmBayVideoShare.bundle/Contents/MacOS/PalmBayVideoShare"))
                {
                    try { Rebuild(); } catch (Exception exception) { UnityEngine.Debug.LogWarning("Mac 视频分享插件未就绪：" + exception.Message); }
                }
            };
        }
        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.StandaloneOSX) Rebuild();
        }
        [MenuItem("PALM BAY/Rebuild Mac video sharing plugin")]
        public static void Rebuild()
        {
            string executable = Root + "/PalmBayVideoShare.bundle/Contents/MacOS/PalmBayVideoShare";
            string source = Root + "/PalmBayVideoShare.mm";
            Directory.CreateDirectory(Path.GetDirectoryName(executable));
            using (Process process = new Process())
            {
                process.StartInfo = new ProcessStartInfo("/usr/bin/xcrun",
                    "clang++ -arch arm64 -arch x86_64 -bundle -fobjc-arc -mmacosx-version-min=11.0 -framework AppKit " +
                    source + " -o " + executable)
                { UseShellExecute = false, RedirectStandardError = true, CreateNoWindow = true };
                process.Start();
                System.Threading.Tasks.Task<string> readError = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(60000)) { process.Kill(); throw new BuildFailedException("Mac 视频分享插件编译超时。"); }
                string error = readError.GetAwaiter().GetResult();
                if (process.ExitCode != 0) throw new BuildFailedException("Mac 视频分享插件编译失败：" + error);
            }
            AssetDatabase.ImportAsset(Root + "/PalmBayVideoShare.bundle", ImportAssetOptions.ForceUpdate);
        }
    }
}
#endif
