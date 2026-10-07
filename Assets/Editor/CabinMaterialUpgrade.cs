using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Repairs generated CabinLobby materials after migrating from Tuanjie to Unity.</summary>
public static class CabinMaterialUpgrade
{
    const string GeneratedFolder = "Assets/Generated/CabinLobby";

    [MenuItem("Palm Bay/Repair CabinLobby materials for Unity 6")]
    public static void Repair()
    {
        Shader standard = Shader.Find("Standard");
        if (standard == null || !standard.isSupported)
        {
            Debug.LogError("CABIN_MATERIAL_REPAIR_FAILED Standard shader is unavailable or unsupported.");
            return;
        }

        string[] materialGuids = AssetDatabase.FindAssets("t:Material", new[] { GeneratedFolder });
        int repaired = 0;
        foreach (string guid in materialGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) continue;

            // Reassign even when the serialized reference looks correct. This makes Unity 6
            // rebuild the material's shader binding instead of reusing Tuanjie's cached state.
            material.shader = standard;
            EditorUtility.SetDirty(material);
            repaired++;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(GeneratedFolder, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
        SceneView.RepaintAll();
        Debug.Log("CABIN_MATERIAL_REPAIR_COMPLETE materials=" + repaired + " shader=" + standard.name);
    }

    [MenuItem("Palm Bay/Validate and capture CabinLobby materials")]
    public static void ValidateAndCapture()
    {
        Repair();
        EditorSceneManager.OpenScene(LobbyBuilder.ScenePath);
        LobbyBuilder.ValidateSavedScene();

        Camera camera = Object.FindAnyObjectByType<Camera>();
        if (camera == null) throw new System.Exception("CabinLobby camera is missing.");

        const int width = 1600;
        const int height = 900;
        const string output = "Evidence/unity6-cabin-material-check.png";
        RenderTexture target = RenderTexture.GetTemporary(width, height, 24);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        try
        {
            camera.targetTexture = target;
            RenderTexture.active = target;
            camera.Render();
            Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            File.WriteAllBytes(output, image.EncodeToPNG());
            Object.DestroyImmediate(image);
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(target);
        }

        Debug.Log("CABIN_MATERIAL_VISUAL_CHECK_COMPLETE path=" + output);
    }
}
