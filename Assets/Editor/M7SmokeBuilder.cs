using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Builds an isolated player while always restoring the normal product identity.</summary>
public static class M7SmokeBuilder
{
    [Serializable] sealed class Identity
    {
        public string protocol = "PALMBAY_M7_PLAYER_SMOKE_V1";
        public string nonce, company, product, bundle, app, evidence, mode, home;
    }
    [MenuItem("Palm Bay/Build isolated M7 smoke player")]
    public static void BuildMacSmoke()
    {
        Build(false);
    }
    [MenuItem("Palm Bay/Build isolated M7 UI smoke player")]
    public static void BuildMacUiSmoke()
    {
        Build(true);
    }
    static void Build(bool uiLaunch)
    {
        const string resource = "Assets/Resources/palmbay-m7-ui-smoke.json";
        // Fail rather than include a leftover auto-run fixture in any build.
        if (File.Exists(resource) || File.Exists(resource + ".meta"))
            throw new Exception("A UI smoke resource already exists; remove or review the stale fixture before building.");
        string company = PlayerSettings.companyName, product = PlayerSettings.productName;
        string bundle = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Standalone);
        var scenes = EditorBuildSettings.scenes;
        bool generatedResource = false, generatedResourcesFolder = false;
        try
        {
            string nonce = Guid.NewGuid().ToString("N");
            var identity = new Identity { nonce = nonce, company = "Palm Bay M7 Smoke " + nonce,
                product = "M7Smoke-" + nonce, bundle = "com.palmbay.m7smoke." + nonce,
                app = Path.GetFullPath(uiLaunch ? "Builds/M7-UI-Smoke-" + nonce + ".app" : "Builds/M7-Smoke.app"),
                mode = uiLaunch ? "ui-launch" : "cli", home = string.Empty };
            if (!File.Exists(DemoBuilder.PalmBayScenePath) || !File.Exists(LobbyBuilder.ScenePath) || !File.Exists(MainLoopBuilder.ScenePath))
                throw new Exception("Create the existing PalmBay, CabinLobby and MainLoop scenes before building M7 smoke.");
            PlayerSettings.companyName = identity.company; PlayerSettings.productName = identity.product;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, identity.bundle);
            DemoBuilder.ConfigureBuildSettings();
            Directory.CreateDirectory("Builds");
            string manifest = uiLaunch ? "Builds/m7-ui-smoke-build.json" : "Builds/m7-smoke-build.json";
            if (File.Exists(manifest)) File.Delete(manifest);
            if (uiLaunch)
            {
                // An explicitly named absolute output path is embedded once in
                // this special player. Existing evidence is never overwritten.
                string requestedEvidence = Environment.GetEnvironmentVariable("PALMBAY_M7_UI_EVIDENCE");
                identity.evidence = Path.GetFullPath(string.IsNullOrEmpty(requestedEvidence) ? "Evidence/m7-ui-player" : requestedEvidence);
                if (Directory.Exists(identity.evidence) && Directory.GetFileSystemEntries(identity.evidence).Length != 0)
                    throw new Exception("UI smoke evidence path is not empty: " + identity.evidence);
                Directory.CreateDirectory(identity.evidence);
                if (!Directory.Exists("Assets/Resources"))
                {
                    Directory.CreateDirectory("Assets/Resources"); generatedResourcesFolder = true;
                }
                generatedResource = true;
                File.WriteAllText(resource, JsonUtility.ToJson(identity, true));
                AssetDatabase.ImportAsset(resource, ImportAssetOptions.ForceSynchronousImport);
            }
            // Existing preprocessors provide the same macOS plugin/shader handling as the normal build.
            var result = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, identity.app, BuildTarget.StandaloneOSX, BuildOptions.None);
            if (result.summary.result != BuildResult.Succeeded) throw new Exception("M7 smoke build failed: " + result.summary.result);
            File.WriteAllText(manifest, JsonUtility.ToJson(identity, true));
            Debug.Log("M7_SMOKE_BUILD_READY mode=" + identity.mode + " app=" + identity.app + " identity=" + nonce + " evidence=" + identity.evidence);
        }
        finally
        {
            try
            {
                if (generatedResource)
                {
                    AssetDatabase.DeleteAsset(resource);
                    if (File.Exists(resource)) File.Delete(resource);
                    if (File.Exists(resource + ".meta")) File.Delete(resource + ".meta");
                }
                if (generatedResourcesFolder && Directory.Exists("Assets/Resources") && Directory.GetFileSystemEntries("Assets/Resources").Length == 0)
                    AssetDatabase.DeleteAsset("Assets/Resources");
            }
            finally
            {
                PlayerSettings.companyName = company; PlayerSettings.productName = product;
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, bundle);
                EditorBuildSettings.scenes = scenes;
                AssetDatabase.SaveAssets();
            }
        }
    }
}
