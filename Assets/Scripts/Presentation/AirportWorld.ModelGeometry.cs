using System.Collections.Generic;
using UnityEngine;

namespace IslandAirport
{
    public static partial class AirportWorld
    {
        static readonly Dictionary<Vector4, Mesh> BevelMeshes = new Dictionary<Vector4, Mesh>();
        static GameObject ModelBox(Transform parent, string name, Vector3 position, Vector3 size, Color color, float bevel, Quaternion rotation, AirportStyle.Finish finish = AirportStyle.Finish.Plastic)
        {
            bevel = Mathf.Min(bevel, Mathf.Min(size.x, Mathf.Min(size.y, size.z)) * 0.49f);
            var key = new Vector4(size.x, size.y, size.z, bevel);
            Mesh mesh;
            if (!BevelMeshes.TryGetValue(key, out mesh) || !mesh)
            {
                var vertices = new List<Vector3>(96);
                var triangles = new List<int>(132);
                Vector3 half = size * 0.5f;
                // Six broad faces, twelve edge bevels and eight corners: 44 triangles.
                for (int axis = 0; axis < 3; axis++)
                {
                    int b = (axis + 1) % 3, c = (axis + 2) % 3;
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        Vector3 p = Vector3.zero;
                        p[axis] = half[axis] * sign;
                        Vector3 u = Vector3.zero, v = Vector3.zero;
                        u[b] = half[b] - bevel;
                        v[c] = half[c] - bevel;
                        AddModelFace(vertices, triangles, p + u + v, p - u + v, p - u - v, p + u - v);
                    }
                    for (int sb = -1; sb <= 1; sb += 2)
                    for (int sc = -1; sc <= 1; sc += 2)
                    {
                        Vector3 p = Vector3.zero, q = Vector3.zero, along = Vector3.zero;
                        p[b] = sb * half[b]; p[c] = sc * (half[c] - bevel);
                        q[b] = sb * (half[b] - bevel); q[c] = sc * half[c];
                        along[axis] = half[axis] - bevel;
                        AddModelFace(vertices, triangles, p - along, q - along, q + along, p + along);
                    }
                }
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                    AddModelFace(vertices, triangles,
                        new Vector3(x * half.x, y * (half.y - bevel), z * (half.z - bevel)),
                        new Vector3(x * (half.x - bevel), y * half.y, z * (half.z - bevel)),
                        new Vector3(x * (half.x - bevel), y * (half.y - bevel), z * half.z));
                mesh = CreateModelMesh("Airport model bevel", vertices, triangles);
                BevelMeshes[key] = mesh;
            }
            var item = new GameObject(name);
            item.transform.SetParent(parent, false);
            item.transform.localPosition = position;
            item.transform.localRotation = rotation;
            item.AddComponent<MeshFilter>().sharedMesh = mesh;
            item.AddComponent<MeshRenderer>().sharedMaterial = AirportStyle.SharedMaterial(color, finish);
            return item;
        }

        static void AddModelFace(List<Vector3> vertices, List<int> triangles, params Vector3[] face)
        {
            int start = vertices.Count;
            Vector3 center = Vector3.zero;
            foreach (Vector3 point in face) { vertices.Add(point); center += point; }
            bool outward = Vector3.Dot(Vector3.Cross(face[1] - face[0], face[2] - face[0]), center) >= 0;
            for (int i = 1; i < face.Length - 1; i++)
            {
                triangles.Add(start);
                triangles.Add(start + (outward ? i : i + 1));
                triangles.Add(start + (outward ? i + 1 : i));
            }
        }

        static Mesh CreateModelMesh(string name, List<Vector3> vertices, List<int> triangles)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

    }
}
