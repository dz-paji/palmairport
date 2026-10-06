using System.Collections.Generic;
using UnityEngine;

namespace IslandAirport
{
    public static partial class AirportWorld
    {
        // Geometry is shared by every flight; airline colors only select shared materials.
        static readonly Dictionary<string, Mesh> AircraftMeshes = new Dictionary<string, Mesh>();
        static readonly Vector2[] AircraftBodyProfile =
        {
            new Vector2(-2.65f, 0), new Vector2(-2.54f, .10f), new Vector2(-2.32f, .20f),
            new Vector2(-2.02f, .28f), new Vector2(-1.66f, .325f), new Vector2(-1.20f, .34f),
            new Vector2(.80f, .34f), new Vector2(1.60f, .34f), new Vector2(2.08f, .325f),
            new Vector2(2.38f, .292f), new Vector2(2.65f, .247f), new Vector2(2.89f, .195f),
            new Vector2(3.10f, .140f), new Vector2(3.28f, .084f), new Vector2(3.41f, .036f),
            new Vector2(3.48f, 0)
        };

        /// <summary>Builds a toy aircraft with a +X nose; position is its ground center.</summary>
        public static Transform CreatePlane(string name, Color color, Vector3 position)
        {
            Transform plane = new GameObject(string.IsNullOrEmpty(name) ? "Island Air Plane" : name).transform;
            plane.position = position;
            plane.rotation = Quaternion.identity;
            Color body = Hex("#FFF9ED"), accentDark = Darken(color, .24f);
            Color trim = Hex("#D5E3DE");
            const float bodyY = 1.03f;

            AircraftLathe(plane, "Fuselage", "body", AircraftBodyProfile,
                new Vector3(0, bodyY, 0), body);
            AircraftSkinDetails(plane, bodyY, color, trim);

            AircraftWing(plane, "Main Wings Left", "wing-left", -1, color);
            AircraftWing(plane, "Main Wings Right", "wing-right", 1, color);
            for (int side = -1; side <= 1; side += 2)
            {
                string suffix = side < 0 ? " Left" : " Right";
                Vector2[] winglet =
                {
                    new Vector2(.05f, 1.10f), new Vector2(.55f, 1.10f),
                    new Vector2(.54f, 1.37f), new Vector2(.38f, 1.42f), new Vector2(.14f, 1.31f)
                };
                AircraftFin(plane, "Winglet" + suffix, "winglet" + side, winglet, side * 1.875f, .04f, accentDark);
                // Separate aileron edge and engine pylon read as manufactured parts.
                ModelBox(plane, "Aileron Edge" + suffix, new Vector3(.08f, 1.106f, side * 1.39f),
                    new Vector3(.045f, .013f, .64f), accentDark, .004f, Quaternion.Euler(0, side * 7, 0));
                ModelBox(plane, "Engine Pylon" + suffix, new Vector3(.31f, .92f, side * 1.04f),
                    new Vector3(.49f, .27f, .085f), body, .035f, Quaternion.identity);
                AircraftEngine(plane, suffix, side, body, accentDark);
            }

            Vector2[] tailPlanform =
            {
                new Vector2(-2.08f, -.13f), new Vector2(-2.68f, -.94f), new Vector2(-2.64f, -1.03f),
                new Vector2(-2.32f, -1.03f), new Vector2(-1.86f, -.10f), new Vector2(-1.86f, .10f),
                new Vector2(-2.32f, 1.03f), new Vector2(-2.64f, 1.03f), new Vector2(-2.68f, .94f), new Vector2(-2.08f, .13f)
            };
            // The tail joins the tapered fuselage, rather than floating behind its cap.
            AircraftCachedPlanform(plane, "Tail Wings", "tail-wings", tailPlanform, 1.02f, 1.11f, accentDark);
            Vector2[] fin =
            {
                new Vector2(-2.58f, 1.12f), new Vector2(-2.12f, 1.27f), new Vector2(-1.95f, 1.97f),
                new Vector2(-2.04f, 2.07f), new Vector2(-2.48f, 1.88f), new Vector2(-2.60f, 1.75f)
            };
            AircraftFin(plane, "Tail Fin", "tail-fin", fin, 0, .16f, color);
            for (int side = -1; side <= 1; side += 2)
                Primitive(plane, "Tail Logo " + side, PrimitiveType.Cylinder,
                    new Vector3(-2.28f, 1.66f, side * .092f), new Vector3(.26f, .012f, .26f), body,
                    Quaternion.Euler(90, 0, 0), AirportStyle.Finish.Plastic);

            AircraftLandingGear(plane, trim);
            TextMesh idLabel = CreateLabel(plane, name, new Vector3(.2f, 1.53f, 0), Window, .0975f);
            idLabel.transform.localRotation = Quaternion.Euler(90, 180, 0);
            return plane;
        }

        static GameObject AircraftMeshObject(Transform parent, string name, Mesh mesh, Vector3 position,
            Color color, AirportStyle.Finish finish = AirportStyle.Finish.Plastic)
        {
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            item.AddComponent<MeshRenderer>().sharedMaterial = AirportStyle.SharedMaterial(color, finish);
            return item;
        }

        static Mesh AircraftFinishMesh(string key, List<Vector3> vertices, List<int> triangles, bool smooth = false)
        {
            var mesh = new Mesh { name = "Aircraft " + key };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            if (smooth)
            {
                // Each rotational ring has a duplicated seam vertex; join its normals.
                Vector3[] normals = mesh.normals;
                for (int ring = 0; ring < vertices.Count / 25; ring++)
                {
                    int a = ring * 25, b = a + 24;
                    normals[a] = normals[b] = (normals[a] + normals[b]).normalized;
                }
                mesh.normals = normals;
            }
            mesh.RecalculateBounds();
            AircraftMeshes[key] = mesh;
            return mesh;
        }

        static GameObject AircraftLathe(Transform parent, string name, string key, Vector2[] profile,
            Vector3 position, Color color, AirportStyle.Finish finish = AirportStyle.Finish.Plastic)
        {
            Mesh mesh;
            if (!AircraftMeshes.TryGetValue(key, out mesh) || !mesh)
            {
                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                for (int ring = 0; ring < profile.Length; ring++)
                for (int i = 0; i <= 24; i++)
                {
                    float angle = i * Mathf.PI * 2 / 24;
                    vertices.Add(new Vector3(profile[ring].x, Mathf.Cos(angle) * profile[ring].y,
                        Mathf.Sin(angle) * profile[ring].y));
                }
                for (int ring = 0; ring < profile.Length - 1; ring++)
                for (int i = 0; i < 24; i++)
                {
                    int a = ring * 25 + i, b = a + 25;
                    triangles.Add(a); triangles.Add(a + 1); triangles.Add(b);
                    triangles.Add(a + 1); triangles.Add(b + 1); triangles.Add(b);
                }
                mesh = AircraftFinishMesh(key, vertices, triangles, true);
            }
            return AircraftMeshObject(parent, name, mesh, position, color, finish);
        }

        static float AircraftBodyRadius(float x)
        {
            for (int i = 1; i < AircraftBodyProfile.Length; i++)
                if (x <= AircraftBodyProfile[i].x)
                    return Mathf.Lerp(AircraftBodyProfile[i - 1].y, AircraftBodyProfile[i].y,
                        Mathf.InverseLerp(AircraftBodyProfile[i - 1].x, AircraftBodyProfile[i].x, x));
            return 0;
        }

        static Vector3 AircraftSkinPoint(float x, float angle, float offset)
        {
            float r = AircraftBodyRadius(x) + offset;
            return new Vector3(x, Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
        }

        static void AircraftSkinPatch(List<Vector3> vertices, List<int> triangles, Vector2[] outline, float offset, int side)
        {
            int start = vertices.Count;
            Vector2 center = Vector2.zero;
            foreach (Vector2 p in outline) center += p;
            center /= outline.Length;
            vertices.Add(AircraftSkinPoint(center.x, center.y * side, offset));
            foreach (Vector2 p in outline) vertices.Add(AircraftSkinPoint(p.x, p.y * side, offset));
            for (int i = 0; i < outline.Length; i++)
            {
                int a = start + 1 + i, b = start + 1 + (i + 1) % outline.Length;
                Vector3 normal = Vector3.Cross(vertices[a] - vertices[start], vertices[b] - vertices[start]);
                Vector3 outward = vertices[start]; outward.x = 0;
                triangles.Add(start);
                triangles.Add(Vector3.Dot(normal, outward) > 0 ? a : b);
                triangles.Add(Vector3.Dot(normal, outward) > 0 ? b : a);
            }
        }

        static Vector2[] AircraftRoundedWindow(float x, float halfWidth, float halfAngle)
        {
            var points = new Vector2[12];
            for (int i = 0; i < points.Length; i++)
            {
                float a = i * Mathf.PI * 2 / points.Length;
                // Superellipse: soft corners with broad, readable straight sides.
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                points[i] = new Vector2(x + Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), .5f) * halfWidth,
                    1.02f + Mathf.Sign(s) * Mathf.Pow(Mathf.Abs(s), .5f) * halfAngle);
            }
            return points;
        }

        static void AircraftSkinDetails(Transform plane, float bodyY, Color accent, Color trim)
        {
            string[] keys = { "livery", "window-rims", "windows", "cockpit" };
            for (int part = 0; part < keys.Length; part++)
            {
                Mesh mesh;
                if (!AircraftMeshes.TryGetValue(keys[part], out mesh) || !mesh)
                {
                    var vertices = new List<Vector3>(); var triangles = new List<int>();
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (part == 0)
                        {
                            // Split along the profile so the stripe hugs the tapered shell.
                            float[] xs = { -2.18f, -2.02f, -1.66f, -1.20f, .80f, 1.60f, 2.08f, 2.38f, 2.57f };
                            for (int i = 1; i < xs.Length; i++)
                                AircraftSkinPatch(vertices, triangles, new[]
                                {
                                    new Vector2(xs[i - 1], 1.66f), new Vector2(xs[i], 1.66f),
                                    new Vector2(xs[i], 1.94f), new Vector2(xs[i - 1], 1.94f)
                                }, .006f, side);
                        }
                        else if (part < 3)
                        {
                            for (int i = 0; i < 7; i++)
                                AircraftSkinPatch(vertices, triangles, AircraftRoundedWindow(-1.36f + i * .44f,
                                    part == 1 ? .145f : .112f, part == 1 ? .27f : .205f), part == 1 ? .009f : .018f, side);
                        }
                        else
                        {
                            // Two swept windshield panels per side, with a visible center pillar.
                            for (int panel = 0; panel < 2; panel++)
                            {
                                float a = panel == 0 ? .09f : .66f, b = panel == 0 ? .57f : 1.16f;
                                AircraftSkinPatch(vertices, triangles, new[]
                                {
                                    new Vector2(2.13f, a), new Vector2(2.58f, a + .06f),
                                    new Vector2(2.70f, b), new Vector2(2.19f, b)
                                }, .014f, side);
                            }
                        }
                    }
                    mesh = AircraftFinishMesh(keys[part], vertices, triangles);
                }
                AircraftMeshObject(plane, part == 3 ? "Cockpit Glass" : keys[part], mesh, new Vector3(0, bodyY, 0),
                    part == 0 ? accent : part == 1 ? trim : Window,
                    part >= 2 ? AirportStyle.Finish.Glass : AirportStyle.Finish.Plastic);
            }
        }

        static void AircraftFace(List<Vector3> vertices, List<int> triangles, Vector3 center, params Vector3[] points)
        {
            int start = vertices.Count;
            Vector3 faceCenter = Vector3.zero;
            foreach (Vector3 point in points) { vertices.Add(point); faceCenter += point; }
            bool outward = Vector3.Dot(Vector3.Cross(points[1] - points[0], points[2] - points[0]),
                faceCenter / points.Length - center) >= 0;
            for (int i = 1; i < points.Length - 1; i++)
            {
                triangles.Add(start); triangles.Add(start + (outward ? i : i + 1));
                triangles.Add(start + (outward ? i + 1 : i));
            }
        }

        static void AircraftWing(Transform plane, string name, string key, int side, Color color)
        {
            Mesh mesh;
            if (!AircraftMeshes.TryGetValue(key, out mesh) || !mesh)
            {
                var vertices = new List<Vector3>(); var triangles = new List<int>();
                float[] spans = { 0, .45f, 1.10f, 1.78f, 1.90f };
                Vector2[] section = { new Vector2(1, 0), new Vector2(.94f, .68f), new Vector2(.72f, 1),
                    new Vector2(.16f, .38f), new Vector2(0, 0), new Vector2(.16f, -.28f),
                    new Vector2(.72f, -.43f), new Vector2(.94f, -.24f) };
                var rings = new Vector3[spans.Length, section.Length];
                for (int r = 0; r < spans.Length; r++)
                {
                    float t = spans[r] / 1.9f;
                    float back = Mathf.Lerp(-.78f, .03f, t), front = Mathf.Lerp(.54f, .66f, t);
                    for (int i = 0; i < section.Length; i++)
                        rings[r, i] = new Vector3(Mathf.Lerp(back, front, section[i].x),
                            1.03f + .07f * t + section[i].y * Mathf.Lerp(.08f, .035f, t), spans[r] * side);
                }
                Vector3 center = new Vector3(.15f, 1.065f, .95f * side);
                for (int r = 0; r < spans.Length - 1; r++)
                for (int i = 0; i < section.Length; i++)
                {
                    int j = (i + 1) % section.Length;
                    AircraftFace(vertices, triangles, center, rings[r, i], rings[r, j], rings[r + 1, j], rings[r + 1, i]);
                }
                for (int r = 0; r < spans.Length; r += spans.Length - 1)
                {
                    var cap = new Vector3[section.Length];
                    for (int i = 0; i < cap.Length; i++) cap[i] = rings[r, i];
                    AircraftFace(vertices, triangles, center, cap);
                }
                mesh = AircraftFinishMesh(key, vertices, triangles);
            }
            AircraftMeshObject(plane, name, mesh, Vector3.zero, color);
        }

        static void AircraftFin(Transform plane, string name, string key, Vector2[] outline, float z, float thickness, Color color)
        {
            Mesh mesh;
            if (!AircraftMeshes.TryGetValue(key, out mesh) || !mesh)
            {
                var vertices = new List<Vector3>(); var triangles = new List<int>();
                Vector3 center = Vector3.zero;
                foreach (Vector2 p in outline) center += new Vector3(p.x, p.y, z);
                center /= outline.Length;
                var front = new Vector3[outline.Length]; var back = new Vector3[outline.Length];
                for (int i = 0; i < outline.Length; i++)
                {
                    front[i] = new Vector3(outline[i].x, outline[i].y, z - thickness * .5f);
                    back[i] = new Vector3(outline[i].x, outline[i].y, z + thickness * .5f);
                }
                AircraftFace(vertices, triangles, center, front); AircraftFace(vertices, triangles, center, back);
                for (int i = 0; i < outline.Length; i++)
                {
                    int j = (i + 1) % outline.Length;
                    AircraftFace(vertices, triangles, center, front[i], front[j], back[j], back[i]);
                }
                mesh = AircraftFinishMesh(key, vertices, triangles);
            }
            AircraftMeshObject(plane, name, mesh, Vector3.zero, color);
        }

        static void AircraftCachedPlanform(Transform plane, string name, string key, Vector2[] outline, float bottom, float top, Color color)
        {
            Mesh mesh;
            if (!AircraftMeshes.TryGetValue(key, out mesh) || !mesh)
            {
                GameObject first = ExtrudedPlanform(plane, name, outline, bottom, top, color);
                AircraftMeshes[key] = first.GetComponent<MeshFilter>().sharedMesh;
            }
            else AircraftMeshObject(plane, name, mesh, Vector3.zero, color);
        }

        static void AircraftEngine(Transform plane, string suffix, int side, Color body, Color accent)
        {
            Vector3 position = new Vector3(0, .76f, side * 1.04f);
            AircraftLathe(plane, "Engine" + suffix, "engine-shell", new[]
            {
                new Vector2(-.26f, .09f), new Vector2(-.16f, .17f), new Vector2(.03f, .205f),
                new Vector2(.62f, .215f), new Vector2(.87f, .23f), new Vector2(.94f, .224f)
            }, position, accent);
            // The rolled lip is an annulus, leaving a real opening and shaded inner duct.
            AircraftLathe(plane, "Intake Lip" + suffix, "intake-lip", new[]
            {
                new Vector2(.88f, .230f), new Vector2(.95f, .224f), new Vector2(.99f, .202f),
                new Vector2(.985f, .173f), new Vector2(.94f, .155f)
            }, position, body);
            AircraftLathe(plane, "Intake Duct" + suffix, "intake-duct", new[]
            {
                new Vector2(.94f, .155f), new Vector2(.74f, .134f), new Vector2(.71f, .13f), new Vector2(.71f, 0)
            }, position, DarkMetal, AirportStyle.Finish.Matte);
            AircraftFanBlades(plane, suffix, position);
            AircraftLathe(plane, "Fan Spinner" + suffix, "fan-spinner", new[]
            {
                new Vector2(.715f, .045f), new Vector2(.77f, .047f), new Vector2(.85f, .018f), new Vector2(.87f, 0)
            }, position, Hex("#8CABA9"));
        }

        static void AircraftFanBlades(Transform plane, string suffix, Vector3 position)
        {
            Mesh mesh;
            if (!AircraftMeshes.TryGetValue("fan-blades", out mesh) || !mesh)
            {
                var vertices = new List<Vector3>(); var triangles = new List<int>();
                for (int blade = 0; blade < 8; blade++)
                {
                    float angle = blade * Mathf.PI * .25f;
                    int start = vertices.Count;
                    float[] radii = { .045f, .128f, .128f, .045f };
                    float[] angles = { angle, angle + .23f, angle + .43f, angle + .20f };
                    for (int i = 0; i < 4; i++)
                        vertices.Add(new Vector3(.735f, Mathf.Cos(angles[i]) * radii[i], Mathf.Sin(angles[i]) * radii[i]));
                    // The fan is recessed into the duct; face toward the +X inlet.
                    triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                    triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
                }
                mesh = AircraftFinishMesh("fan-blades", vertices, triangles);
            }
            AircraftMeshObject(plane, "Fan Blades" + suffix, mesh, position, Hex("#8CABA9"));
        }

        static void AircraftLandingGear(Transform plane, Color trim)
        {
            float[] x = { 1.26f, -.72f, -.72f }, z = { 0, -.55f, .55f };
            string[] names = { "Nose", "Left Main", "Right Main" };
            for (int i = 0; i < 3; i++)
            {
                ModelBox(plane, names[i] + " Gear Strut", new Vector3(x[i], .46f, z[i]),
                    new Vector3(.075f, .45f, .065f), trim, .02f, Quaternion.identity);
                ModelBox(plane, names[i] + " Gear Door", new Vector3(x[i] + .09f, .65f, z[i]),
                    new Vector3(.25f, .13f, .11f), Hex("#FFF9ED"), .025f, Quaternion.Euler(0, 0, -12));
                float radius = i == 0 ? .13f : .15f;
                // Rounded tire shoulders replace the abrupt cylinder edges.
                GameObject tire = AircraftLathe(plane, names[i] + " Wheel", i == 0 ? "nose-tire" : "main-tire", new[]
                {
                    new Vector2(-.08f, 0), new Vector2(-.08f, radius * .76f), new Vector2(-.055f, radius),
                    new Vector2(.055f, radius), new Vector2(.08f, radius * .76f), new Vector2(.08f, 0)
                }, new Vector3(x[i], .20f, z[i]), DarkMetal, AirportStyle.Finish.Matte);
                tire.transform.localRotation = Quaternion.Euler(0, 90, 0);
                for (int side = -1; side <= 1; side += 2)
                    Primitive(plane, names[i] + " Wheel Hub " + side, PrimitiveType.Cylinder,
                        new Vector3(x[i], .20f, z[i] + side * .082f), new Vector3(radius * .95f, .008f, radius * .95f),
                        trim, Quaternion.Euler(90, 0, 0), AirportStyle.Finish.Plastic);
            }
        }
    }
}
