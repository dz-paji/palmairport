using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using IslandAirport;

public static class DemoBuilder
{
    public const string PalmBayScenePath="Assets/Scenes/PalmBay.unity";
    public const string DeliveryVersion="1.0.9";

    static void RequireProductBuild()
    {
        if(File.Exists("Assets/Resources/palmbay-m7-ui-smoke.json") ||
           PlayerSettings.companyName.StartsWith("Palm Bay M7 Smoke ",StringComparison.Ordinal))
            throw new Exception("Smoke fixture identity/resource is active; restore the product configuration before building a delivery package.");
    }

    [MenuItem("Palm Bay/Rebuild PalmBay scene (product)")]
    public static void CreatePalmBayScene()
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var root=new GameObject("Palm Bay - Airport Demo");
        root.AddComponent<AirportGame>();
        EditorSceneManager.SaveScene(scene,PalmBayScenePath);
        ConfigureBuildSettings();
        Debug.Log("PALM_BAY_SCENE_READY path="+PalmBayScenePath);
    }

    [MenuItem("Palm Bay/Rebuild product scenes")]
    public static void CreateScene()
    {
        LobbyBuilder.CreateScene();
        CreatePalmBayScene();
        MainLoopBuilder.CreateScene();
        EditorSceneManager.OpenScene(LobbyBuilder.ScenePath);
        ConfigureBuildSettings();
        Debug.Log("PALM_BAY_SCENES_READY lobby="+LobbyBuilder.ScenePath+" product="+PalmBayScenePath+" debug="+MainLoopBuilder.ScenePath);
    }

    [MenuItem("Palm Bay/Validate saved scenes and capture MainLoop")]
    public static void ValidateScenes()
    {
        // Rebuild first so this command proves the complete generation → save
        // → reload path rather than validating a scene left by another task.
        CreateScene();
        LobbyBuilder.ValidateSavedScene();
        MainLoopBuilder.ValidateSavedScene(true);
        ValidatePalmBayScene();
        LevelLayoutValidation.Validate();
        ConfigureBuildSettings();
        EditorSceneManager.OpenScene(LobbyBuilder.ScenePath);
        Debug.Log("PALM_BAY_SCENES_VALIDATED capture=Evidence/main-loop-scene.png");
    }

    static void ValidatePalmBayScene()
    {
        var scene=EditorSceneManager.OpenScene(PalmBayScenePath);
        if(!scene.IsValid())throw new Exception("Could not reopen "+PalmBayScenePath);
        int missing=0;
        foreach(var root in scene.GetRootGameObjects())missing+=GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(root);
        if(missing>0)throw new Exception("Missing scripts in PalmBay: "+missing);
        var game=UnityEngine.Object.FindAnyObjectByType<AirportGame>();
        if(!game)throw new Exception("PalmBay scene is missing AirportGame");
        Debug.Log("PALM_BAY_SCENE_VALIDATED roots="+scene.rootCount);
    }

    public static void ConfigureBuildSettings()
    {
        // CabinLobby is the product entry point; PalmBay and MainLoop remain local playtest scenes.
        EditorBuildSettings.scenes=new[]{
            new EditorBuildSettingsScene(LobbyBuilder.ScenePath,true),
            new EditorBuildSettingsScene(PalmBayScenePath,true),
            new EditorBuildSettingsScene(MainLoopBuilder.ScenePath,true)
        };
    }

    [MenuItem("Palm Bay/Build macOS demo")]
    public static void BuildMac()
    {
        RequireProductBuild();
        PlayerSettings.bundleVersion=DeliveryVersion;
        if(!File.Exists(PalmBayScenePath))CreatePalmBayScene();
        if(!File.Exists(LobbyBuilder.ScenePath))LobbyBuilder.CreateScene();
        if(!File.Exists(MainLoopBuilder.ScenePath))MainLoopBuilder.CreateScene();
        ConfigureBuildSettings();
        Directory.CreateDirectory("Builds");
        var result=BuildPipeline.BuildPlayer(EditorBuildSettings.scenes,"Builds/Palm Bay.app",BuildTarget.StandaloneOSX,BuildOptions.None);
        if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new System.Exception("Build failed: "+result.summary.result);
        Debug.Log("PALM_BAY_BUILD_READY entry=CabinLobby scenes="+EditorBuildSettings.scenes.Length);
    }

    /// <summary>
    /// M1 真机验收包：锁横屏、包名 com.palmbay.islandairport、MinSdk 26、IL2CPP+ARM64。
    /// 未安装 Android Build Support 时给出明确中文报错。
    /// </summary>
    [MenuItem("Palm Bay/Build Android demo")]
    public static void BuildAndroid()
    {
        RequireProductBuild();
        if(!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android,BuildTarget.Android))
            throw new Exception("未安装 Android Build Support 模块；请先在 Unity Hub 为该编辑器安装 Android 模块。");
        // 当前激活平台不是 Android 时，架构等 PlayerSettings 可能不被构建前置校验读取；
        // 先切平台再做设置，并落盘后再构建。
        if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.Android)
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android,BuildTarget.Android);
        if(!File.Exists(PalmBayScenePath))CreatePalmBayScene();
        if(!File.Exists(LobbyBuilder.ScenePath))LobbyBuilder.CreateScene();
        if(!File.Exists(MainLoopBuilder.ScenePath))MainLoopBuilder.CreateScene();
        ConfigureBuildSettings();
        PlayerSettings.productName="PALM BAY";
        PlayerSettings.bundleVersion=DeliveryVersion;
        PlayerSettings.Android.bundleVersionCode=9;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android,"com.palmbay.islandairport");
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android,ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.minSdkVersion=AndroidSdkVersions.AndroidApiLevel26;
        PlayerSettings.Android.targetArchitectures=AndroidArchitecture.ARM64;
        PlayerSettings.Android.forceInternetPermission=true;
        PlayerSettings.defaultInterfaceOrientation=UIOrientation.LandscapeLeft;
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Builds");
        var result=BuildPipeline.BuildPlayer(EditorBuildSettings.scenes,"Builds/PalmBay.apk",BuildTarget.Android,BuildOptions.None);
        if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new System.Exception("Build failed: "+result.summary.result);
        Debug.Log("PALM_BAY_ANDROID_READY path=Builds/PalmBay.apk scenes="+EditorBuildSettings.scenes.Length);
    }
}
