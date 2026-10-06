using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using IslandAirport;

/// <summary>Unobstructed renders of the actual runtime factories, not concept art.</summary>
public static class ModelPolishCapture
{
    const string Output = "Evidence/visual-polish/after";
    [MenuItem("Palm Bay/Capture visual polish - runtime models")]
    public static void Run()
    {
        Directory.CreateDirectory(Output);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        AirportWorld.SetupLighting();
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Review floor";
        floor.transform.position = new Vector3(0, -.07f, 0);
        floor.transform.localScale = new Vector3(100, .1f, 100);
        floor.GetComponent<Renderer>().sharedMaterial = AirportStyle.SharedMaterial(AirportStyle.Hex("E4E5D8"));
        UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
        var cam = new GameObject("Review Camera").AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = AirportStyle.Hex("DCE5E3");
        cam.orthographic = true; cam.nearClipPlane = .1f; cam.farClipPlane = 100;
        cam.allowHDR = false;
        var report = new StringBuilder("Runtime model factory review\n");
        foreach (ServiceKind kind in new[] { ServiceKind.Meals, ServiceKind.Baggage, ServiceKind.Fuel })
        {
            Transform model = AirportWorld.CreateCart(kind, kind.ToString(), Vector3.zero);
            AirportWorld.CreateCargo(kind, model).gameObject.SetActive(true);
            Capture(cam, model, "model-" + kind.ToString().ToLowerInvariant(), report);
            UnityEngine.Object.DestroyImmediate(model.gameObject);
        }
        var aircraft = AirportWorld.CreatePlane("PALM BAY", AirportStyle.Baggage, Vector3.zero);
        Capture(cam, aircraft, "model-aircraft", report);
        UnityEngine.Object.DestroyImmediate(aircraft.gameObject);
        var crew = AirportWorld.CreateCrew("Ground crew", AirportStyle.Teal, Vector3.zero);
        Capture(cam, crew, "model-crew", report);
        UnityEngine.Object.DestroyImmediate(crew.gameObject);
        var passengers = new GameObject("Passengers").transform;
        for (int i = 0; i < 4; i++)
            AirportWorld.CreatePassenger("Passenger " + i, AirportStyle.Hex(i % 2 == 0 ? "EAB5BE" : "9EADCD"), new Vector3(i * 1.0f - 1.5f, 0, 0), i);
        foreach (Transform p in UnityEngine.Object.FindObjectsOfType<Transform>()) if (p.name.StartsWith("Passenger ") && p.parent == null) p.SetParent(passengers);
        Capture(cam, passengers, "model-passengers", report);
        UnityEngine.Object.DestroyImmediate(passengers.gameObject);
        File.WriteAllText(Path.Combine(Output, "model-review.txt"), report.ToString() + "PASS: factory creation, nonempty geometry, finite bounds, no colliders.\nNot a device performance test.\n");
        Debug.Log("MODEL_POLISH_CAPTURE_PASSED");
    }
    static void Capture(Camera cam, Transform model, string name, StringBuilder report)
    {
        // Gameplay flight labels are intentionally hidden only for this geometry inspection.
        foreach (var label in model.GetComponentsInChildren<TextMesh>()) label.gameObject.SetActive(false);
        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) throw new Exception("Empty model " + name);
        Bounds bounds = renderers[0].bounds;
        int triangles = 0;
        foreach (var renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
            if (!renderer.sharedMaterial) throw new Exception("Missing material " + name);
        }
        foreach (var mesh in model.GetComponentsInChildren<MeshFilter>())
        {
            if (!mesh.sharedMesh) throw new Exception("Missing mesh " + name);
            triangles += mesh.sharedMesh.triangles.Length / 3;
        }
        if (float.IsNaN(bounds.size.sqrMagnitude) || float.IsInfinity(bounds.size.sqrMagnitude) || bounds.size.sqrMagnitude == 0)
            throw new Exception("Invalid bounds " + name);
        if (model.GetComponentsInChildren<Collider>().Length != 0) throw new Exception("Visual model introduced colliders " + name);
        cam.transform.position = bounds.center + new Vector3(6, 4.3f, 7).normalized * 25;
        cam.transform.LookAt(bounds.center);
        cam.aspect = 1.6f;
        float spanX = 0, spanY = 0;
        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
        {
            Vector3 corner = Vector3.Scale(bounds.extents, new Vector3(x, y, z));
            spanX = Mathf.Max(spanX, Mathf.Abs(Vector3.Dot(corner, cam.transform.right)));
            spanY = Mathf.Max(spanY, Mathf.Abs(Vector3.Dot(corner, cam.transform.up)));
        }
        cam.orthographicSize = Mathf.Max(spanY, spanX / cam.aspect) * 1.22f;
        var target = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 };
        var pixels = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
        var oldActive = RenderTexture.active;
        try
        {
            cam.targetTexture = target; cam.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); pixels.Apply();
            File.WriteAllBytes(Path.Combine(Output, name + ".png"), pixels.EncodeToPNG());
            report.AppendLine(name + ": renderers=" + renderers.Length + ", triangles=" + triangles + ", bounds=" + bounds.size);
        }
        finally
        {
            cam.targetTexture = null; RenderTexture.active = oldActive;
            UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
