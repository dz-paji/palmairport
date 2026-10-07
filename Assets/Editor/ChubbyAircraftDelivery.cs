using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using IslandAirport;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds a portable, script-free aircraft from the runtime factory.
/// Run in a Built-in Render Pipeline project with -executeMethod ChubbyAircraftDelivery.Run.
/// Uses a temporary additive scene, so interactive runs preserve all open scenes.
/// </summary>
public static class ChubbyAircraftDelivery
{
    const string Art = "Assets/Art/ChubbyAircraft";
    const string PrefabPath = Art + "/ChubbyAircraft.prefab";
    const string Evidence = "Evidence/chubby-aircraft";
    const int ReviewLayer = 31;
    const int Width = 1600, Height = 1100;
    static readonly string[] PaletteNames = AirportWorld.AircraftPaletteNames;
    static readonly string[] ConfigurationNames = AirportWorld.AircraftConfigurationNames;
    static readonly Dictionary<string, string> ExpectedMeshHashes = new Dictionary<string, string>();

    sealed class MaterialGroup
    {
        public Material Source;
        public readonly List<CombineInstance> Parts = new List<CombineInstance>();
    }

    [MenuItem("Palm Bay/Export chubby aircraft model and previews")]
    public static void Run()
    {
        ExpectedMeshHashes.Clear();
        if (GraphicsSettings.currentRenderPipeline != null)
            throw new Exception("ChubbyAircraftDelivery requires the Built-in Render Pipeline for its Standard materials.");
        Shader standard = Shader.Find("Standard");
        if (!standard) throw new Exception("Built-in Standard shader is unavailable.");
        if (!Application.isBatchMode)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            // Unity cannot add a scene beside any unsaved untitled scene, even a pristine one.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (string.IsNullOrEmpty(open.path) && !EditorSceneManager.SaveScene(open)) return;
            }
        }
        for (int palette = 0; palette < PaletteNames.Length; palette++)
        for (int configuration = 0; configuration < ConfigurationNames.Length; configuration++)
        {
            string folder = Art + "/" + VariantName(palette, configuration);
            Directory.CreateDirectory(folder + "/Meshes");
            Directory.CreateDirectory(folder + "/Materials");
        }
        Directory.CreateDirectory(Evidence);
        Directory.CreateDirectory("Builds/ChubbyAircraft");
        AssetDatabase.Refresh();

        Scene previous = SceneManager.GetActiveScene();
        Scene review = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
            Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
        SceneManager.SetActiveScene(review);
        Material floorMaterial = null;
        var report = new StringBuilder("Rounded aircraft delivery: 4 fictional livery palettes x B737-800 / A380-800 / de Havilland Comet 4 = 12 variants\n");
        try
        {
            ValidateVariantSelection(report);
            Camera camera = new GameObject("Aircraft Review Camera").AddComponent<Camera>();
            camera.cullingMask = 1 << ReviewLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.82f, .87f, .87f);
            camera.orthographic = true;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.nearClipPlane = .05f;
            camera.farClipPlane = 500;
            var models = new List<GameObject>();
            var modelBounds = new List<Bounds>();
            GameObject floor = null;
            float maxLength = 0, maxSpan = 0;
            for (int palette = 0; palette < PaletteNames.Length; palette++)
            for (int configuration = 0; configuration < ConfigurationNames.Length; configuration++)
            {
                string variant = VariantName(palette, configuration);
                string folder = Art + "/" + variant;
                report.AppendLine("\n" + variant + " (palette=" + palette + ", configuration=" + configuration + ", engines=" + (configuration == 0 ? 2 : 4) + ")");
                Transform source = AirportWorld.CreatePlaneVariant("", palette, configuration, Vector3.zero);
                foreach (TextMesh label in source.GetComponentsInChildren<TextMesh>(true))
                    UnityEngine.Object.DestroyImmediate(label.gameObject);
                ValidateSourceConfiguration(source, configuration, report);
                Bounds sourceBounds = Validate(source.gameObject, false, report, "Factory");
                GameObject model = CombineAndPersist(source, standard, folder, variant, report);
                UnityEngine.Object.DestroyImmediate(source.gameObject);
                Bounds bounds = Validate(model, true, report, "Combined");
                if ((bounds.center - sourceBounds.center).sqrMagnitude > .000001f ||
                    (bounds.size - sourceBounds.size).sqrMagnitude > .000001f)
                    throw new Exception("Combining changed the aircraft bounds: " + variant);
                SaveAndValidatePrefab(model, folder + "/" + variant + ".prefab", report);
                if (palette == 0 && configuration == 0) SaveAndValidatePrefab(model, PrefabPath, report);
                ExportObj(model, "Builds/ChubbyAircraft/" + variant + ".obj", "Builds/ChubbyAircraft/" + variant + ".mtl");
                if (!floor) floor = PrepareReview(bounds, standard, out floorMaterial);
                SetLayer(model.transform);
                Capture(camera, bounds, new Vector3(10, 5, 2.3f), variant + ".png");
                Capture(camera, bounds, Vector3.right, variant + "-front.png");
                Capture(camera, bounds, Vector3.forward, variant + "-side.png");
                Capture(camera, bounds, Vector3.up, variant + "-top.png");
                if (palette == 0 && configuration == 0)
                {
                    Capture(camera, bounds, new Vector3(10, 5, 2.3f), "front-three-quarter.png");
                    Capture(camera, bounds, Vector3.right, "front.png");
                    Capture(camera, bounds, new Vector3(-7, 4.5f, -8), "rear-three-quarter.png");
                    Capture(camera, bounds, Vector3.forward, "side.png");
                    Capture(camera, bounds, Vector3.up, "top.png");
                }
                models.Add(model);
                modelBounds.Add(bounds);
                maxLength = Mathf.Max(maxLength, bounds.size.x);
                maxSpan = Mathf.Max(maxSpan, bounds.size.z);
                model.SetActive(false);
            }
            ValidateAircraftIdentity(modelBounds, report);
            WriteComparisonBoards(camera, models, modelBounds, floor, report);
            WriteGallery(modelBounds);
            Bounds fleetBounds = new Bounds();
            bool haveFleetBounds = false;
            for (int i = 0; i < models.Count; i++)
            {
                int palette = i / ConfigurationNames.Length, configuration = i % ConfigurationNames.Length;
                Vector3 center = new Vector3((configuration - 1) * (maxLength + 3.0f), 0,
                    (palette - 1.5f) * (maxSpan + 2.5f));
                // Center each model in its cell while retaining its ground Y and +X nose.
                models[i].transform.position = center - new Vector3(modelBounds[i].center.x, 0, modelBounds[i].center.z);
                models[i].SetActive(true);
                foreach (Renderer renderer in models[i].GetComponentsInChildren<Renderer>())
                {
                    if (!haveFleetBounds) { fleetBounds = renderer.bounds; haveFleetBounds = true; }
                    else fleetBounds.Encapsulate(renderer.bounds);
                }
            }
            floor.transform.position = new Vector3(fleetBounds.center.x, fleetBounds.min.y - .105f, fleetBounds.center.z);
            float floorExtent = Mathf.Max(fleetBounds.size.x, fleetBounds.size.z) * 4;
            floor.transform.localScale = new Vector3(floorExtent, .2f, floorExtent);
            Capture(camera, fleetBounds, new Vector3(9, 22, 14), "fleet-overview.png");
            // Explicit curated paths exclude older ShortTwin/MediumTwin/LongQuad folders even if present.
            var packagePaths = new List<string> { PrefabPath };
            for (int palette = 0; palette < PaletteNames.Length; palette++)
            for (int configuration = 0; configuration < ConfigurationNames.Length; configuration++)
                packagePaths.Add(Art + "/" + VariantName(palette, configuration));
            if (File.Exists(Art + "/README.md")) packagePaths.Add(Art + "/README.md");
            AssetDatabase.ExportPackage(packagePaths.ToArray(), "Builds/ChubbyAircraft.unitypackage", ExportPackageOptions.Recurse);
            report.AppendLine("PASS: finite meshes, nonempty triangles, no colliders/scripts, grounded bounds, persistent assets, exact SHA-256 vertex/normal/index roundtrip, prefab reload, isolated dependencies.");
            report.AppendLine("Unity: " + Application.unityVersion);
            report.AppendLine("Prefabs: 12 palette/configuration subdirectories; default alias: " + PrefabPath);
            report.AppendLine("Package: Builds/ChubbyAircraft.unitypackage (only " + Art + ")");
            report.AppendLine("OBJ: Builds/ChubbyAircraft/<variant>.obj + .mtl; meters, +X nose, +Y up, mirrored Z for right-handed OBJ.");
            report.AppendLine("Preview: actual mesh Camera.Render at " + Width + "x" + Height + "; each variant has front three-quarter, front, true side and top closeups plus a common-scale comparison.");
            report.AppendLine("Review: gallery.html; identity-lineup.png and identity-front.png (one blue palette, three aircraft, same meters/pixel); catalogue-board.png (4 palette rows x 3 aircraft columns); fleet-overview.png.");
            report.AppendLine("Aircraft shapes and livery layouts are stylized fictional tributes; the 12 combinations are not claims about historical airline fleets.");
            report.AppendLine("No text labels, airline names, logos or script dependencies are included on the models.");
            report.AppendLine("Material: Built-in Standard; URP/HDRP projects must convert materials after import.");
            report.AppendLine("Bounds validation is geometric; this is not a device performance test.");
            File.WriteAllText(Evidence + "/delivery-report.txt", report.ToString());
            Debug.Log("CHUBBY_AIRCRAFT_DELIVERY_PASSED\n" + report);
        }
        finally
        {
            if (floorMaterial) UnityEngine.Object.DestroyImmediate(floorMaterial);
            if (!Application.isBatchMode)
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                if (review.IsValid() && review.isLoaded) EditorSceneManager.CloseScene(review, true);
            }
        }
    }

    static string VariantName(int palette, int configuration)
    {
        return PaletteNames[palette] + "_" + ConfigurationNames[configuration];
    }

    static void ValidateVariantSelection(StringBuilder report)
    {
        if (PaletteNames.Length != 4 || ConfigurationNames.Length != 3)
            throw new Exception("Expected four aircraft palettes and three configurations.");
        var covered = new HashSet<int>();
        for (int i = 0; i < 128; i++)
        {
            string id = "TEST" + i.ToString(CultureInfo.InvariantCulture);
            int index = AirportWorld.AircraftVariantIndex(id);
            if (index < 0 || index >= 12 || index != AirportWorld.AircraftVariantIndex(id))
                throw new Exception("Unstable/out-of-range aircraft variant selection: " + id);
            covered.Add(index);
        }
        if (covered.Count != 12) throw new Exception("TEST0 through TEST127 do not cover all twelve aircraft variants.");
        foreach (string id in new[] { "", "Flight Alpha", "QZ-12345678901234567890", "航班", "TEST0" })
        {
            int index = AirportWorld.AircraftVariantIndex(id);
            if (index < 0 || index >= 12 || index != AirportWorld.AircraftVariantIndex(id))
                throw new Exception("Unstable/out-of-range aircraft variant selection: " + id);
        }
        var openingConfigurations = new HashSet<int>();
        foreach (string id in new[] { "UBA826", "QMR152", "AZU407" })
            openingConfigurations.Add(AirportWorld.AircraftVariantIndex(id) / 4);
        if (openingConfigurations.Count != 3) throw new Exception("Opening flights must show all three aircraft types.");
        report.AppendLine("Variant selection PASS: stable, in [0,11], TEST0..TEST127 cover all 12.");
    }

    static void ValidateSourceConfiguration(Transform source, int configuration, StringBuilder report)
    {
        int engines = 0, nestedEngines = 0, portEngines = 0, starboardEngines = 0;
        foreach (Transform part in source.GetComponentsInChildren<Transform>(true))
        {
            if (part.name.StartsWith("Engine Assembly", StringComparison.Ordinal))
            {
                engines++;
                float side = source.InverseTransformPoint(part.position).z;
                if (side < -.05f) portEngines++;
                else if (side > .05f) starboardEngines++;
                for (Transform ancestor = part.parent; ancestor && ancestor != source; ancestor = ancestor.parent)
                    if (ancestor.name.IndexOf("Wing", StringComparison.OrdinalIgnoreCase) >= 0)
                    { nestedEngines++; break; }
            }
            if (part.name.IndexOf("Logo", StringComparison.OrdinalIgnoreCase) >= 0 ||
                part.name.IndexOf("Badge", StringComparison.OrdinalIgnoreCase) >= 0)
                throw new Exception("Branding geometry is forbidden: " + part.name);
        }
        int expected = configuration == 0 ? 2 : 4;
        if (engines != expected) throw new Exception("Expected " + expected + " engine assemblies, got " + engines);
        if (portEngines != expected / 2 || starboardEngines != expected / 2)
            throw new Exception("Expected symmetric engine assemblies: got " + portEngines + "/" + starboardEngines);
        if (configuration == 2 && nestedEngines != 4)
            throw new Exception("Comet must have four engine assemblies nested beneath wing-root objects, got " + nestedEngines);
        if (source.GetComponentsInChildren<TextMesh>(true).Length != 0)
            throw new Exception("Text labels remained on source aircraft.");
        report.AppendLine("Source configuration PASS: " + engines + " engine assemblies (left/right=" + portEngines + "/" + starboardEngines +
            ", wing-nested=" + nestedEngines + "); no Logo/Badge objects or TextMesh labels.");
    }

    static void SaveAndValidatePrefab(GameObject model, string path, StringBuilder report)
    {
        bool saved;
        PrefabUtility.SaveAsPrefabAsset(model, path, out saved);
        if (!saved) throw new Exception("Could not save aircraft prefab: " + path);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        GameObject reloaded = PrefabUtility.LoadPrefabContents(path);
        try { Validate(reloaded, true, report, "Reloaded " + Path.GetFileName(path)); }
        finally { PrefabUtility.UnloadPrefabContents(reloaded); }
        foreach (string dependency in AssetDatabase.GetDependencies(path, true))
            if (dependency.StartsWith("Assets/", StringComparison.Ordinal) &&
                !dependency.StartsWith(Art + "/", StringComparison.Ordinal))
                throw new Exception("Prefab has an external project dependency: " + dependency);
    }

    static GameObject CombineAndPersist(Transform source, Shader standard, string folder, string variant, StringBuilder report)
    {
        var groups = new List<MaterialGroup>();
        var byMaterial = new Dictionary<Material, MaterialGroup>();
        int sourceParts = 0;
        foreach (MeshFilter filter in source.GetComponentsInChildren<MeshFilter>(true))
        {
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (!renderer || !renderer.enabled || !filter.gameObject.activeInHierarchy) continue;
            Mesh mesh = filter.sharedMesh;
            if (!mesh) throw new Exception("Missing source mesh: " + filter.name);
            Material[] materials = renderer.sharedMaterials;
            if (materials.Length != mesh.subMeshCount)
                throw new Exception("Unexpected material/submesh mapping: " + filter.name);
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                Material material = materials[submesh];
                if (!material) throw new Exception("Missing source material: " + filter.name);
                if (material.mainTexture) throw new Exception("Texture export is not supported: " + material.name);
                MaterialGroup group;
                if (!byMaterial.TryGetValue(material, out group))
                {
                    group = new MaterialGroup { Source = material };
                    byMaterial.Add(material, group);
                    groups.Add(group);
                }
                group.Parts.Add(new CombineInstance
                {
                    mesh = mesh, subMeshIndex = submesh,
                    transform = source.worldToLocalMatrix * filter.transform.localToWorldMatrix
                });
            }
            sourceParts++;
        }
        var result = new GameObject(variant);
        for (int i = 0; i < groups.Count; i++)
        {
            MaterialGroup group = groups[i];
            string id = "Part_" + i.ToString("D2", CultureInfo.InvariantCulture);
            int count = 0;
            foreach (CombineInstance part in group.Parts) count += part.mesh.vertexCount;
            var mesh = new Mesh
            {
                name = variant + "_" + id,
                indexFormat = count <= 65535 ? IndexFormat.UInt16 : IndexFormat.UInt32
            };
            mesh.CombineMeshes(group.Parts.ToArray(), true, true, false);
            mesh.RecalculateBounds();
            Mesh persistentMesh = Persist(mesh, folder + "/Meshes/" + id + ".asset");
            var material = new Material(group.Source) { name = variant + "_" + id, shader = standard, enableInstancing = true };
            Material persistentMaterial = Persist(material, folder + "/Materials/" + id + ".mat");
            var child = new GameObject(id + "_" + ColorUtility.ToHtmlStringRGB(persistentMaterial.color));
            child.transform.SetParent(result.transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = persistentMesh;
            MeshRenderer renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = persistentMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }
        report.AppendLine("Material batching: " + sourceParts + " source mesh objects -> " + groups.Count + " renderers/materials.");
        return result;
    }

    static T Persist<T>(T generated, string path) where T : UnityEngine.Object
    {
        T existing = AssetDatabase.LoadAssetAtPath<T>(path);
        Mesh generatedMesh = generated as Mesh;
        if (generatedMesh) ExpectedMeshHashes[path] = MeshGeometryHash(generatedMesh);
        if (!existing)
        {
            if (generatedMesh) generatedMesh.UploadMeshData(false);
            AssetDatabase.CreateAsset(generated, path);
            return generated;
        }
        Mesh existingMesh = existing as Mesh;
        if (generatedMesh && existingMesh)
        {
            // CopySerialized can leave a previously rendered Mesh's native GPU buffers stale
            // after its vertex/index counts change. Update through the Mesh API to invalidate
            // those buffers while retaining the asset object, GUID and prefab references.
            existingMesh.Clear(false);
            existingMesh.indexFormat = generatedMesh.indexFormat;
            existingMesh.name = generatedMesh.name;
            existingMesh.SetVertices(generatedMesh.vertices);
            if (generatedMesh.normals.Length != 0) existingMesh.SetNormals(generatedMesh.normals);
            if (generatedMesh.tangents.Length != 0) existingMesh.SetTangents(generatedMesh.tangents);
            if (generatedMesh.colors.Length != 0) existingMesh.SetColors(generatedMesh.colors);
            for (int channel = 0; channel < 8; channel++)
            {
                VertexAttribute attribute = (VertexAttribute)((int)VertexAttribute.TexCoord0 + channel);
                if (!generatedMesh.HasVertexAttribute(attribute)) continue;
                int dimension = generatedMesh.GetVertexAttributeDimension(attribute);
                if (dimension == 2)
                {
                    var uv = new List<Vector2>();
                    generatedMesh.GetUVs(channel, uv);
                    existingMesh.SetUVs(channel, uv);
                }
                else if (dimension == 3)
                {
                    var uv = new List<Vector3>();
                    generatedMesh.GetUVs(channel, uv);
                    existingMesh.SetUVs(channel, uv);
                }
                else
                {
                    var uv = new List<Vector4>();
                    generatedMesh.GetUVs(channel, uv);
                    existingMesh.SetUVs(channel, uv);
                }
            }
            existingMesh.subMeshCount = generatedMesh.subMeshCount;
            for (int submesh = 0; submesh < generatedMesh.subMeshCount; submesh++)
                existingMesh.SetIndices(generatedMesh.GetIndices(submesh, false), generatedMesh.GetTopology(submesh),
                    submesh, false, (int)generatedMesh.GetBaseVertex(submesh));
            existingMesh.bounds = generatedMesh.bounds;
            existingMesh.UploadMeshData(false);
            if (MeshGeometryHash(existingMesh) != ExpectedMeshHashes[path])
                throw new Exception("Explicit mesh persistence changed geometry: " + path);
        }
        else EditorUtility.CopySerialized(generated, existing);
        EditorUtility.SetDirty(existing);
        UnityEngine.Object.DestroyImmediate(generated);
        return existing;
    }

    static string MeshGeometryHash(Mesh mesh)
    {
        // Exact float/index bytes catch CPU geometry changes across asset save/import and
        // prefab reload. Camera previews additionally exercise the newly uploaded GPU data.
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write((int)mesh.indexFormat);
            Vector3[] vertices = mesh.vertices;
            writer.Write(vertices.Length);
            foreach (Vector3 vertex in vertices)
            { writer.Write(vertex.x); writer.Write(vertex.y); writer.Write(vertex.z); }
            Vector3[] normals = mesh.normals;
            writer.Write(normals.Length);
            foreach (Vector3 normal in normals)
            { writer.Write(normal.x); writer.Write(normal.y); writer.Write(normal.z); }
            writer.Write(mesh.subMeshCount);
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                writer.Write((int)mesh.GetTopology(submesh));
                int[] indices = mesh.GetIndices(submesh);
                writer.Write(indices.Length);
                foreach (int index in indices) writer.Write(index);
            }
            writer.Flush();
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "");
        }
    }

    static Bounds Validate(GameObject root, bool persistent, StringBuilder report, string stage)
    {
        if (root.GetComponentsInChildren<Collider>(true).Length != 0) throw new Exception(stage + ": visual model has colliders.");
        if (root.GetComponentsInChildren<MonoBehaviour>(true).Length != 0) throw new Exception(stage + ": model has script dependencies.");
        if (root.GetComponentsInChildren<TextMesh>(true).Length != 0) throw new Exception(stage + ": labels were not removed.");
        if (root.transform.localPosition != Vector3.zero || root.transform.localRotation != Quaternion.identity || root.transform.localScale != Vector3.one)
            throw new Exception(stage + ": model root transform must be identity.");
        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
        if (filters.Length == 0) throw new Exception(stage + ": no meshes.");
        Bounds bounds = new Bounds();
        bool haveBounds = false;
        int vertices = 0, triangles = 0;
        foreach (MeshFilter filter in filters)
        {
            Mesh mesh = filter.sharedMesh;
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (!mesh || !renderer || mesh.vertexCount == 0 || mesh.triangles.Length == 0)
                throw new Exception(stage + ": empty/missing mesh or renderer: " + filter.name);
            if (mesh.normals.Length != mesh.vertexCount)
                throw new Exception(stage + ": mesh is missing vertex normals: " + filter.name);
            foreach (Vector3 vertex in mesh.vertices) RequireFinite(vertex, stage + " vertex");
            foreach (Vector3 normal in mesh.normals) RequireFinite(normal, stage + " normal");
            foreach (int index in mesh.triangles)
                if (index < 0 || index >= mesh.vertexCount) throw new Exception(stage + ": invalid triangle index.");
            if (mesh.triangles.Length % 3 != 0) throw new Exception(stage + ": triangle array length is invalid.");
            foreach (Material material in renderer.sharedMaterials)
            {
                if (!material || !material.shader) throw new Exception(stage + ": missing material/shader.");
                if (persistent && !AssetDatabase.Contains(material)) throw new Exception(stage + ": material is not persistent.");
                if (persistent && material.shader.name != "Standard") throw new Exception(stage + ": material is not Built-in Standard.");
            }
            if (persistent && !AssetDatabase.Contains(mesh)) throw new Exception(stage + ": mesh is not persistent.");
            if (persistent)
            {
                string path = AssetDatabase.GetAssetPath(mesh), expectedHash;
                if (!ExpectedMeshHashes.TryGetValue(path, out expectedHash) || MeshGeometryHash(mesh) != expectedHash)
                    throw new Exception(stage + ": persisted vertex/normal/index data differs from generated mesh: " + path);
            }
            vertices += mesh.vertexCount;
            triangles += mesh.triangles.Length / 3;
            if (!haveBounds) { bounds = renderer.bounds; haveBounds = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        RequireFinite(bounds.center, stage + " bounds center");
        RequireFinite(bounds.size, stage + " bounds size");
        if (bounds.size.x <= 0 || bounds.size.y <= 0 || bounds.size.z <= 0) throw new Exception(stage + ": collapsed bounds.");
        if (bounds.min.y < -.01f || bounds.min.y > .10f)
            throw new Exception(stage + ": wheels must meet the ground center within 0.10m, minimum Y=" + bounds.min.y);
        report.AppendLine(stage + ": renderers=" + filters.Length + ", vertices=" + vertices + ", triangles=" + triangles +
            ", bounds=" + bounds.size.ToString("F3") + ", minimum Y=" + bounds.min.y.ToString("F4", CultureInfo.InvariantCulture));
        return bounds;
    }

    static void RequireFinite(Vector3 value, string context)
    {
        if (float.IsNaN(value.x) || float.IsInfinity(value.x) || float.IsNaN(value.y) || float.IsInfinity(value.y) ||
            float.IsNaN(value.z) || float.IsInfinity(value.z)) throw new Exception("Nonfinite " + context);
    }

    static void SetLayer(Transform root)
    {
        root.gameObject.layer = ReviewLayer;
        foreach (Transform child in root) SetLayer(child);
    }

    static GameObject PrepareReview(Bounds bounds, Shader standard, out Material floorMaterial)
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.58f, .61f, .65f);
        RenderSettings.fog = false;
        RenderSettings.skybox = null;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Neutral review floor";
        floor.layer = ReviewLayer;
        floor.transform.position = new Vector3(bounds.center.x, bounds.min.y - .105f, bounds.center.z);
        float extent = Mathf.Max(bounds.size.x, bounds.size.z) * 10;
        floor.transform.localScale = new Vector3(extent, .2f, extent);
        UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
        floorMaterial = new Material(standard) { color = new Color(.69f, .74f, .75f) };
        floorMaterial.SetFloat("_Glossiness", .08f);
        floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
        AddLight("Warm key", Quaternion.Euler(45, -30, 0), new Color(1, .93f, .83f), 1.0f, true);
        AddLight("Cool fill", Quaternion.Euler(35, 150, 0), new Color(.77f, .88f, 1), .34f, false);
        return floor;
    }

    static void AddLight(string name, Quaternion rotation, Color color, float intensity, bool shadows)
    {
        Light light = new GameObject(name).AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = rotation;
        light.color = color;
        light.intensity = intensity;
        light.cullingMask = 1 << ReviewLayer;
        light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        light.shadowStrength = .45f;
        light.shadowBias = .035f;
    }

    static void ValidateAircraftIdentity(List<Bounds> bounds, StringBuilder report)
    {
        for (int palette = 0; palette < PaletteNames.Length; palette++)
        {
            Vector3 b737 = bounds[palette * 3].size;
            Vector3 a380 = bounds[palette * 3 + 1].size;
            Vector3 comet = bounds[palette * 3 + 2].size;
            if (a380.x < b737.x * 1.25f || a380.z < b737.z * 1.35f || a380.y < b737.y * 1.15f)
                throw new Exception("A380 must be substantially longer, wider and taller than B737: " + PaletteNames[palette]);
            if (comet.x >= b737.x - .10f || comet.y >= b737.y || comet.z >= a380.z)
                throw new Exception("Comet must be shorter/lower than B737 and narrower than A380: " + PaletteNames[palette]);
            if (palette > 0)
                for (int configuration = 0; configuration < ConfigurationNames.Length; configuration++)
                    if ((bounds[palette * 3 + configuration].size - bounds[configuration].size).sqrMagnitude > .0025f)
                        throw new Exception("Livery changed aircraft dimensions: " + VariantName(palette, configuration));
            report.AppendLine("Identity PASS " + PaletteNames[palette] + ": B737 L/H/span=" + b737.ToString("F3") +
                "; A380=" + a380.ToString("F3") + "; Comet=" + comet.ToString("F3") + ". Length order Comet < B737 < A380.");
        }
    }

    static void WriteComparisonBoards(Camera camera, List<GameObject> models, List<Bounds> bounds, GameObject floor, StringBuilder report)
    {
        float length = 0, span = 0, top = 0;
        for (int i = 0; i < models.Count; i++)
        {
            length = Mathf.Max(length, bounds[i].size.x);
            span = Mathf.Max(span, bounds[i].size.z);
            top = Mathf.Max(top, bounds[i].max.y);
            models[i].transform.position = new Vector3(-bounds[i].center.x, 0, -bounds[i].center.z);
            models[i].SetActive(false);
        }
        Bounds sharedFrame = new Bounds(new Vector3(0, top * .5f, 0), new Vector3(length, top, span));
        floor.transform.position = new Vector3(0, -.105f, 0);
        floor.transform.localScale = new Vector3(Mathf.Max(length, span) * 10, .2f, Mathf.Max(length, span) * 10);
        const int tileWidth = 800, tileHeight = 550;
        var catalogue = new Texture2D(tileWidth * 3, tileHeight * 4, TextureFormat.RGB24, false);
        var lineup = new Texture2D(tileWidth * 3, tileHeight, TextureFormat.RGB24, false);
        var front = new Texture2D(tileWidth * 3, tileHeight, TextureFormat.RGB24, false);
        Vector3 direction = new Vector3(10, 5, 2.3f);
        try
        {
            for (int i = 0; i < models.Count; i++)
            {
                int palette = i / 3, configuration = i % 3;
                models[i].SetActive(true);
                Capture(camera, sharedFrame, direction, VariantName(palette, configuration) + "-compare.png");
                Texture2D tile = Render(camera, sharedFrame, direction, tileWidth, tileHeight);
                try
                {
                    Color[] pixels = tile.GetPixels();
                    catalogue.SetPixels(configuration * tileWidth, (3 - palette) * tileHeight, tileWidth, tileHeight, pixels);
                    if (palette == 0) lineup.SetPixels(configuration * tileWidth, 0, tileWidth, tileHeight, pixels);
                }
                finally { UnityEngine.Object.DestroyImmediate(tile); }
                if (palette == 0)
                {
                    tile = Render(camera, sharedFrame, Vector3.right, tileWidth, tileHeight);
                    try { front.SetPixels(configuration * tileWidth, 0, tileWidth, tileHeight, tile.GetPixels()); }
                    finally { UnityEngine.Object.DestroyImmediate(tile); }
                }
                models[i].SetActive(false);
            }
            WritePng(catalogue, "catalogue-board.png");
            WritePng(lineup, "identity-lineup.png");
            WritePng(front, "identity-front.png");
            report.AppendLine("Common comparison frame L/H/span=" + sharedFrame.size.ToString("F3") +
                "; identical angle, orthographic camera size and meters/pixel in all 12 comparison tiles. Lineup palette=" + PaletteNames[0] + ".");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(catalogue);
            UnityEngine.Object.DestroyImmediate(lineup);
            UnityEngine.Object.DestroyImmediate(front);
        }
    }

    static string AircraftTitle(int configuration)
    {
        return new[] { "Boeing 737-800", "Airbus A380-800", "de Havilland Comet 4" }[configuration];
    }

    static string Dimensions(Bounds bounds)
    {
        return "L " + bounds.size.x.ToString("F2", CultureInfo.InvariantCulture) +
            " / span " + bounds.size.z.ToString("F2", CultureInfo.InvariantCulture) +
            " / H " + bounds.size.y.ToString("F2", CultureInfo.InvariantCulture) + " model units";
    }

    static void WriteGallery(List<Bounds> bounds)
    {
        var html = new StringBuilder("<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>Rounded aircraft mesh review</title>");
        html.Append("<style>body{font:16px/1.5 system-ui,sans-serif;background:#f3f6f7;color:#183044;margin:0 auto;padding:32px;max-width:1800px}h1,h2{line-height:1.2}.grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:16px}.columns{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:16px;text-align:center}.card{margin:0;background:white;border-radius:12px;overflow:hidden;border:1px solid #d9e2e7}img{display:block;width:100%;height:auto}figcaption{padding:12px}small{display:block;color:#527080}a{color:#14659a}.views{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:8px}summary{cursor:pointer;padding:12px;font-weight:600}section{margin:40px 0}.badge{display:inline-block;background:#dce8ed;padding:4px 10px;border-radius:20px}nav{display:flex;gap:18px;flex-wrap:wrap}</style>");
        html.Append("<h1>Three distinct rounded aircraft</h1><p>Actual Unity mesh renders. Four fictional livery layouts, without airline names or logos. The models are stylized; their dimensions are not a scale specification for real aircraft.</p><nav><a href=\"#identity\">Aircraft identity</a><a href=\"#catalogue\">4 × 3 catalogue</a><a href=\"#closeups\">Front / side / top closeups</a><a href=\"delivery-report.txt\">Validation report</a></nav>");
        html.Append("<section id=\"identity\"><h2>Same palette, same camera scale</h2><p>Blue palette: camera direction and model units per pixel are identical in every cell. The A380 retains its larger length, wingspan and height.</p><div class=\"columns\">");
        for (int configuration = 0; configuration < 3; configuration++)
            html.Append("<p><strong>" + AircraftTitle(configuration) + "</strong><small>" + (configuration == 0 ? "2 underwing engines" : configuration == 1 ? "4 underwing engines · double deck" : "4 wing-root engines · vintage swept wings") + "</small><small>" + Dimensions(bounds[configuration]) + "</small></p>");
        html.Append("</div><a href=\"identity-lineup.png\"><img src=\"identity-lineup.png\" alt=\"Three aircraft at identical scale, high front three-quarter angle\"></a><h3>True front: engine placement and span</h3><a href=\"identity-front.png\"><img src=\"identity-front.png\" alt=\"737, A380 and Comet, true frontal views at identical scale\"></a></section>");
        html.Append("<section id=\"catalogue\"><h2>Four palettes × three aircraft</h2><p>Rows use " + string.Join(", ", PaletteNames) + "; columns use B737-800, A380-800, Comet 4. All twelve images share the same framing and scale. <a href=\"catalogue-board.png\">Open complete PNG board</a>.</p><div class=\"columns\">");
        for (int configuration = 0; configuration < 3; configuration++) html.Append("<strong>" + AircraftTitle(configuration) + "</strong>");
        html.Append("</div>");
        for (int palette = 0; palette < 4; palette++)
        {
            html.Append("<h3>" + PaletteNames[palette] + "</h3><div class=\"grid\">");
            for (int configuration = 0; configuration < 3; configuration++)
            {
                string variant = VariantName(palette, configuration);
                html.Append("<figure class=\"card\"><a href=\"" + variant + "-compare.png\"><img src=\"" + variant + "-compare.png\" alt=\"" + variant + " at common comparison scale\"></a><figcaption><strong>" + AircraftTitle(configuration) + "</strong><small>" + Dimensions(bounds[palette * 3 + configuration]) + "</small></figcaption></figure>");
            }
            html.Append("</div>");
        }
        html.Append("</section><section id=\"closeups\"><h2>Individual inspection views</h2><p>These closeups fit each aircraft individually. Use the common-scale catalogue above to compare sizes. Front looks directly toward the +X nose; side looks directly along Z; top looks down Y.</p>");
        for (int palette = 0; palette < 4; palette++)
        for (int configuration = 0; configuration < 3; configuration++)
        {
            string variant = VariantName(palette, configuration);
            html.Append("<details class=\"card\"><summary>" + PaletteNames[palette] + " — " + AircraftTitle(configuration) + "</summary><div class=\"views\">");
            string[] suffixes = { "", "-front", "-side", "-top" };
            string[] labels = { "High front three-quarter", "True front", "True side", "Top" };
            for (int view = 0; view < 4; view++)
                html.Append("<figure><a href=\"" + variant + suffixes[view] + ".png\"><img src=\"" + variant + suffixes[view] + ".png\" alt=\"" + labels[view] + " view of " + variant + "\"></a><figcaption>" + labels[view] + "</figcaption></figure>");
            html.Append("</div></details>");
        }
        html.Append("</section><p>Built-in Standard materials. Mesh-only prefabs and OBJ exports. <a href=\"fleet-overview.png\">Complete physical fleet arrangement</a>.</p></html>");
        File.WriteAllText(Evidence + "/gallery.html", html.ToString());
    }

    static void Capture(Camera camera, Bounds bounds, Vector3 direction, string filename)
    {
        Texture2D texture = Render(camera, bounds, direction, Width, Height);
        try { WritePng(texture, filename); }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    }

    static void WritePng(Texture2D texture, string filename)
    {
        texture.Apply();
        File.WriteAllBytes(Evidence + "/" + filename, texture.EncodeToPNG());
    }

    static Texture2D Render(Camera camera, Bounds bounds, Vector3 direction, int width, int height)
    {
        float distance = Mathf.Max(20, bounds.size.magnitude * 3);
        camera.transform.position = bounds.center + direction.normalized * distance;
        camera.transform.rotation = Quaternion.LookRotation(-direction.normalized,
            Mathf.Abs(direction.normalized.y) > .99f ? Vector3.right : Vector3.up);
        camera.aspect = (float)width / height;
        float horizontal = 0, vertical = 0;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 corner = Vector3.Scale(bounds.extents, new Vector3(x, y, z));
            horizontal = Mathf.Max(horizontal, Mathf.Abs(Vector3.Dot(corner, camera.transform.right)));
            vertical = Mathf.Max(vertical, Mathf.Abs(Vector3.Dot(corner, camera.transform.up)));
        }
        camera.orthographicSize = Mathf.Max(vertical, horizontal / camera.aspect) * 1.18f;
        var target = new RenderTexture(width, height, 24) { antiAliasing = 4 };
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            return texture;
        }
        catch
        {
            UnityEngine.Object.DestroyImmediate(texture);
            throw;
        }
        finally
        {
            camera.targetTexture = previousTarget;
            RenderTexture.active = previous;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    static void ExportObj(GameObject model, string objPath, string mtlPath)
    {
        var obj = new StringBuilder("# ChubbyAircraft. Meters; +X nose, +Y up; right-handed Z.\nmtllib " + Path.GetFileName(mtlPath) + "\n");
        var mtl = new StringBuilder("# Solid color material approximation of Built-in Standard.\n");
        int offset = 1, group = 0;
        foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>())
        {
            Mesh mesh = filter.sharedMesh;
            Material material = filter.GetComponent<MeshRenderer>().sharedMaterial;
            string id = "Material_" + group++;
            obj.AppendLine("o " + filter.name);
            obj.AppendLine("usemtl " + id);
            foreach (Vector3 vertex in mesh.vertices) obj.AppendLine("v " + F(vertex.x) + " " + F(vertex.y) + " " + F(-vertex.z));
            foreach (Vector3 normal in mesh.normals) obj.AppendLine("vn " + F(normal.x) + " " + F(normal.y) + " " + F(-normal.z));
            int[] indices = mesh.triangles;
            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = indices[i] + offset, b = indices[i + 2] + offset, c = indices[i + 1] + offset;
                obj.AppendLine("f " + a + "//" + a + " " + b + "//" + b + " " + c + "//" + c);
            }
            offset += mesh.vertexCount;
            Color color = material.color;
            mtl.AppendLine("newmtl " + id);
            mtl.AppendLine("Kd " + F(color.r) + " " + F(color.g) + " " + F(color.b));
            mtl.AppendLine("Ks 0.15 0.15 0.15\nNs 32\nd 1\nillum 2\n");
        }
        File.WriteAllText(objPath, obj.ToString());
        File.WriteAllText(mtlPath, mtl.ToString());
    }

    static string F(float value) { return value.ToString("G9", CultureInfo.InvariantCulture); }
}
