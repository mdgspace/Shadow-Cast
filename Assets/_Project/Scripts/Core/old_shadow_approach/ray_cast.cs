//this class returns the public variables shadow_points and shadow_distances
//respectively, a list of 2d vectors and list of floats.

using System.Collections.Generic;
using UnityEngine;

public class ray_cast : MonoBehaviour
{
    private object_manager manager_script;
    private List<MeshFilter> objects;
    private List<Light> lights;
    private MeshFilter wall;
    private List<List<Vector3>> normals;
    private List<List<Vector3>> centers;

    public List<Vector2> shadow_points;
    public List<float> shadow_distances;
    public struct EdgeData
    {
        public Vector3 localPosA;
        public Vector3 localPosB;
        public int faceIndex1;
        public int faceIndex2;
    }

    private List<EdgeData[]> allObjectEdges = new List<EdgeData[]>();


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
        Physics.queriesHitBackfaces = true;

        shadow_points = new List<Vector2>();
        shadow_distances = new List<float>();

        InitializeData();
    }

    public void InitializeData()
    {
        if (manager_script == null)
            manager_script = GetComponent<object_manager>();
        if (manager_script == null)
        {
            Debug.LogError("[ray_cast] An object_manager component is required.", this);
            return;
        }
        objects = manager_script.objects;
        lights = manager_script.lights;
        wall = manager_script.wall;

        normals = new List<List<Vector3>>();
        centers = new List<List<Vector3>>();
        allObjectEdges = new List<EdgeData[]>();

        if (objects == null) return;

        foreach (MeshFilter meshfilter in objects)
        {
            if (meshfilter == null || meshfilter.sharedMesh == null)
                continue;

            Mesh mesh = meshfilter.sharedMesh;
            int[] triangles = mesh.triangles;
            Vector3[] vertices = mesh.vertices;

            List<Vector3> normals_object = new List<Vector3>();
            List<Vector3> centers_object = new List<Vector3>();

            
            Dictionary<Vector3Int, int> posToCanonicalId = new Dictionary<Vector3Int, int>();
            List<Vector3> uniquePositions = new List<Vector3>();
            int[] vertexToCanonical = new int[vertices.Length];

            for (int v = 0; v < vertices.Length; v++)
            {
                Vector3 pos = vertices[v];
                Vector3Int key = new Vector3Int(
                    Mathf.RoundToInt(pos.x * 10000f),
                    Mathf.RoundToInt(pos.y * 10000f),
                    Mathf.RoundToInt(pos.z * 10000f)
                );

                if (!posToCanonicalId.TryGetValue(key, out int canonicalId))
                {
                    canonicalId = uniquePositions.Count;
                    posToCanonicalId[key] = canonicalId;
                    uniquePositions.Add(pos);
                }
                vertexToCanonical[v] = canonicalId;
            }

           
            Dictionary<Vector2Int, List<int>> edgeDictionary = new Dictionary<Vector2Int, List<int>>();

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int faceIndex = i / 3;

                int idx1 = triangles[i];
                int idx2 = triangles[i + 1];
                int idx3 = triangles[i + 2];

                Vector3 v1 = vertices[idx1];
                Vector3 v2 = vertices[idx2];
                Vector3 v3 = vertices[idx3];

                Vector3 center = (v1 + v2 + v3) / 3f;
                Vector3 normal = Vector3.Cross(v2 - v1, v3 - v1).normalized;

                normals_object.Add(normal);
                centers_object.Add(center);

                int c1 = vertexToCanonical[idx1];
                int c2 = vertexToCanonical[idx2];
                int c3 = vertexToCanonical[idx3];

                int[,] edges = new int[3, 2] {
                    { c1, c2 },
                    { c2, c3 },
                    { c3, c1 }
                };

                for (int e = 0; e < 3; e++)
                {
                    int a = edges[e, 0];
                    int b = edges[e, 1];

    
                    if (a == b) continue;

                    Vector2Int edgeKey = new Vector2Int(Mathf.Min(a, b), Mathf.Max(a, b));

                    if (!edgeDictionary.TryGetValue(edgeKey, out List<int> faceList))
                    {
                        faceList = new List<int>();
                        edgeDictionary[edgeKey] = faceList;
                    }
                    faceList.Add(faceIndex);
                }
            }

            normals.Add(normals_object);
            centers.Add(centers_object);

            EdgeData[] objectEdges = new EdgeData[edgeDictionary.Count];
            int edgeIdx = 0;

            foreach (KeyValuePair<Vector2Int, List<int>> kvp in edgeDictionary)
            {
                Vector2Int edgeKey = kvp.Key;
                List<int> adjacentFaces = kvp.Value;

                int f1 = adjacentFaces[0];
                    
                int f2 = (adjacentFaces.Count > 1) ? adjacentFaces[1] : f1;

                objectEdges[edgeIdx] = new EdgeData
                {
                    localPosA = uniquePositions[edgeKey.x],
                    localPosB = uniquePositions[edgeKey.y],
                    faceIndex1 = f1,
                    faceIndex2 = f2
                };

                edgeIdx++;
            }

            allObjectEdges.Add(objectEdges);
        }
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        if (shadow_points == null) shadow_points = new List<Vector2>();
        if (shadow_distances == null) shadow_distances = new List<float>();
        shadow_points.Clear();
        shadow_distances.Clear();

        if (manager_script == null)
            manager_script = GetComponent<object_manager>();

        if (manager_script == null) return;


        if (wall == null && manager_script.wall != null)
            wall = manager_script.wall;

        if (lights == null || lights.Count == 0)
            lights = manager_script.lights;

        if (objects == null || objects.Count == 0)
            objects = manager_script.objects;


        if (allObjectEdges.Count == 0 && objects != null && objects.Count > 0)
        {
            InitializeData();
        }

        if (lights == null || objects == null || wall == null || allObjectEdges.Count == 0)
            return;


        int wallLayerMask = 1 << wall.gameObject.layer;

        foreach (Light light in lights)
        {
            if (light == null) continue;

            int j = -1;
            float max_dist = Mathf.Max(50f, (light.transform.position - wall.transform.position).magnitude * 5f);

            foreach (MeshFilter mesh in objects)
            {
                j += 1;
                if (mesh == null || mesh.sharedMesh == null || j >= allObjectEdges.Count)
                    continue;

                Vector3 light_pos = mesh.transform.worldToLocalMatrix.MultiplyPoint(light.transform.position);


                int triangleCount = mesh.sharedMesh.triangles.Length / 3;
                List<int> illuminated = new List<int>(triangleCount);

                for (int i = 0; i < mesh.sharedMesh.triangles.Length; i += 3)
                {
                    int faceIdx = i / 3;
                    Vector3 light_dir = light_pos - centers[j][faceIdx];
                    float dotprod = Vector3.Dot(light_dir, normals[j][faceIdx]);

                    illuminated.Add(dotprod > 0 ? 1 : 0);
                }

               
                List<Vector3> silhouette = new List<Vector3>();

                foreach (EdgeData edge in allObjectEdges[j])
                {
                    if (illuminated[edge.faceIndex1] != illuminated[edge.faceIndex2])
                    {
                        silhouette.Add(mesh.transform.localToWorldMatrix.MultiplyPoint(edge.localPosA));
                        silhouette.Add(mesh.transform.localToWorldMatrix.MultiplyPoint(edge.localPosB));
                    }
                }


                List<Vector3> uniqueItems = new List<Vector3>(new HashSet<Vector3>(silhouette));

                //actual raycasting
                //wall object must be in layer 6, named "wall"

                foreach (Vector3 vert in uniqueItems){
                    Vector3 light_vec = light.transform.position - vert;
                    Vector3 direction = -light_vec.normalized;

                  
                    Vector3 rayOrigin = vert + direction * 0.002f;
                    RaycastHit hit;

                    if (Physics.Raycast(rayOrigin, direction, out hit, max_dist, wallLayerMask))
                    {
                        Vector3 local_3d = wall.transform.worldToLocalMatrix.MultiplyPoint(hit.point);

                        Vector2 local_pt;
                        if (wall.sharedMesh != null && wall.sharedMesh.bounds.size.y < wall.sharedMesh.bounds.size.z)
                        {
                            local_pt = new Vector2(local_3d.x, local_3d.z);
                        }
                        else
                        {
                            local_pt = new Vector2(local_3d.x, local_3d.y);
                        }

                        shadow_points.Add(local_pt);
                        shadow_distances.Add(hit.distance + light_vec.magnitude);

                        
                        Debug.DrawLine(vert, hit.point, Color.cyan);
                    }
                }
            }
        }
    }
}
