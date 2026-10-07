using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using IslandAirport;

public static class LobbyBuilder
{
    public const string ScenePath = "Assets/Scenes/CabinLobby.unity";
    const string GeneratedFolder = "Assets/Generated/CabinLobby";

    [MenuItem("Palm Bay/Rebuild CabinLobby scene")]
    public static void CreateScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("Cabin Lobby");
        root.AddComponent<LobbyApp>();
        root.AddComponent<LobbyCanvas>();
        CabinWorld.SetupLighting(root.transform);
        CabinWorld.Build(root.transform);

        var cameraObject = new GameObject("Cabin Lobby Camera");
        cameraObject.transform.SetParent(root.transform, false);
        var camera = cameraObject.AddComponent<Camera>();
        camera.tag = "MainCamera";
        cameraObject.AddComponent<AudioListener>();
        CabinWorld.ConfigureCamera(camera);
        PersistGeneratedAssets(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        DemoBuilder.ConfigureBuildSettings();
        Debug.Log("CABIN_LOBBY_SCENE_READY path=" + ScenePath);
    }

    public static void ValidateSavedScene()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath);
        if (!scene.IsValid()) throw new Exception("Could not reopen " + ScenePath);
        int missing = 0;
        int meshes = 0;
        int materials = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(root);
            foreach (var mesh in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mesh.sharedMesh || !AssetDatabase.Contains(mesh.sharedMesh))
                    throw new Exception("CabinLobby has a missing or non-persistent mesh on " + mesh.name);
                meshes++;
            }
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                {
                    if (!material || !AssetDatabase.Contains(material))
                        throw new Exception("CabinLobby has a missing or non-persistent material on " + renderer.name);
                    materials++;
                }
        }
        if (missing > 0) throw new Exception("Missing scripts in CabinLobby: " + missing);
        var app = UnityEngine.Object.FindAnyObjectByType<LobbyApp>();
        var canvas = UnityEngine.Object.FindAnyObjectByType<LobbyCanvas>();
        var camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
        if (!app || !canvas || !camera) throw new Exception("CabinLobby app, canvas, or camera reference is missing");
        if (meshes < 50 || materials < 50) throw new Exception("CabinLobby cabin geometry is incomplete; meshes=" + meshes + " materials=" + materials);
        Debug.Log("CABIN_LOBBY_SCENE_VALIDATED roots=" + scene.rootCount + " meshes=" + meshes + " materials=" + materials);
    }

    static void PersistGeneratedAssets(UnityEngine.SceneManagement.Scene scene)
    {
        // Each rebuild writes a fresh asset set; stale ones from earlier builds only bloat the project.
        if (Directory.Exists(GeneratedFolder)) AssetDatabase.DeleteAsset(GeneratedFolder);
        Directory.CreateDirectory(GeneratedFolder);
        AssetDatabase.Refresh();
        int index = 0;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var mesh in root.GetComponentsInChildren<MeshFilter>(true))
                if (mesh.sharedMesh && !AssetDatabase.Contains(mesh.sharedMesh))
                    AssetDatabase.CreateAsset(mesh.sharedMesh, AssetDatabase.GenerateUniqueAssetPath(GeneratedFolder + "/Mesh_" + (index++) + ".asset"));
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (material && !AssetDatabase.Contains(material))
                        AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath(GeneratedFolder + "/Material_" + (index++) + ".mat"));
        }
        AssetDatabase.SaveAssets();
    }
}
