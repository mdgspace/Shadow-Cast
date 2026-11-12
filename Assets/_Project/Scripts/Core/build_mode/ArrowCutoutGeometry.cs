using System.Collections.Generic;
using UnityEngine;

namespace ShadowCast.Core.BuildMode
{
    /// <summary>Creates a cube-shaped mesh with a real triangular prism subtracted through its depth.</summary>
    public static class ArrowCutoutGeometry
    {
        public static Mesh CreateMesh(Vector3 size, Vector2 holeSize)
        {
            float halfX = Mathf.Max(0.05f, size.x * 0.5f);
            float halfY = Mathf.Max(0.05f, size.y * 0.5f);
            float halfZ = Mathf.Max(0.005f, size.z * 0.5f);
            float arrowX = Mathf.Clamp(holeSize.x * 0.5f, 0.01f, halfX * 0.8f);
            float arrowY = Mathf.Clamp(holeSize.y * 0.5f, 0.01f, halfY * 0.8f);

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            // These three convex prisms are exactly the cube volume minus a right-pointing triangular prism.
            AddPrism(new[]
            {
                new Vector2(-halfX, -halfY), new Vector2(-arrowX, -halfY),
                new Vector2(-arrowX, halfY), new Vector2(-halfX, halfY)
            }, halfZ, vertices, triangles);
            AddPrism(new[]
            {
                new Vector2(-arrowX, arrowY), new Vector2(arrowX, 0f),
                new Vector2(halfX, 0f), new Vector2(halfX, halfY), new Vector2(-arrowX, halfY)
            }, halfZ, vertices, triangles);
            AddPrism(new[]
            {
                new Vector2(-arrowX, -halfY), new Vector2(halfX, -halfY),
                new Vector2(halfX, 0f), new Vector2(arrowX, 0f), new Vector2(-arrowX, -arrowY)
            }, halfZ, vertices, triangles);

            Mesh mesh = new Mesh { name = "Generated_ArrowBooleanCube" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Vector2[][] CreateColliderPaths(Vector2 size, Vector2 holeSize)
        {
            float halfX = size.x * 0.5f;
            float halfY = size.y * 0.5f;
            float arrowX = Mathf.Clamp(holeSize.x * 0.5f, 0.01f, halfX * 0.8f);
            float arrowY = Mathf.Clamp(holeSize.y * 0.5f, 0.01f, halfY * 0.8f);
            return new[]
            {
                new[] { new Vector2(-halfX,-halfY), new Vector2(-arrowX,-halfY), new Vector2(-arrowX,halfY), new Vector2(-halfX,halfY) },
                new[] { new Vector2(-arrowX,arrowY), new Vector2(arrowX,0f), new Vector2(halfX,0f), new Vector2(halfX,halfY), new Vector2(-arrowX,halfY) },
                new[] { new Vector2(-arrowX,-halfY), new Vector2(halfX,-halfY), new Vector2(halfX,0f), new Vector2(arrowX,0f), new Vector2(-arrowX,-arrowY) }
            };
        }

        private static void AddPrism(IReadOnlyList<Vector2> polygon, float halfDepth, List<Vector3> vertices, List<int> triangles)
        {
            int start = vertices.Count;
            int count = polygon.Count;
            for (int i = 0; i < count; i++) vertices.Add(new Vector3(polygon[i].x, polygon[i].y, -halfDepth));
            for (int i = 0; i < count; i++) vertices.Add(new Vector3(polygon[i].x, polygon[i].y, halfDepth));

            for (int i = 1; i < count - 1; i++)
            {
                triangles.Add(start); triangles.Add(start + i + 1); triangles.Add(start + i);
                triangles.Add(start + count); triangles.Add(start + count + i); triangles.Add(start + count + i + 1);
            }

            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                int a = start + i;
                int b = start + next;
                int c = start + count + next;
                int d = start + count + i;
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
                triangles.Add(a); triangles.Add(c); triangles.Add(d);
            }
        }
    }
}
