using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(convex_hull))]
public class collider_n_shade : MonoBehaviour
{
    [Header("Dependencies")]
    [Tooltip("The wall mesh filter on which shadow platforms are projected.")]
    public MeshFilter wall;

    [Header("Visual Appearance")]
    [Tooltip("The shadow color (default is black).")]
    public Color shadowColor = Color.black;

    [Tooltip("Optional custom material. If not assigned, one will be created using Custom/ShadowPlatform.")]
    public Material customMaterial;

    [Tooltip("Optional custom shader. If not assigned, Custom/ShadowPlatform will be loaded.")]
    public Shader shadowShader;

    [Tooltip("Slight Z offset in front of the wall to eliminate Z-fighting.")]
    public float zOffset = -0.002f;

    [Header("Distance Mapping")]
    [Tooltip("If true, automatically adjusts maxDistance based on the distance between the light source and the wall.")]
    public bool autoAdjustDistanceRange = true;

    [Tooltip("Average distance considered 'closest' (results in sharpest edge, highest alpha).")]
    public float minDistance = 0.2f;

    [Tooltip("Average distance considered 'farthest' (results in softest edge, lowest alpha).")]
    public float maxDistance = 15.0f;

    [Header("Alpha Settings")]
    [Range(0f, 1f)]
    [Tooltip("Alpha opacity when object is close to the wall.")]
    public float maxAlpha = 1.0f;

    [Range(0f, 1f)]
    [Tooltip("Alpha opacity when object is far from the wall.")]
    public float minAlpha = 0.35f;

    [Header("Edge Softening Settings")]
    [Tooltip("Edge feather width in local wall units when close (sharp edge).")]
    public float minEdgeSoftness = 0.01f;

    [Tooltip("Edge feather width in local wall units when far (soft penumbra edge).")]
    public float maxEdgeSoftness = 0.4f;

    [Range(0.2f, 4f)]
    [Tooltip("Falloff exponent for the edge softening transition.")]
    public float softnessPower = 1.0f;

    [Header("Collider Settings")]
    [Tooltip("Whether to generate a PolygonCollider2D on each platform.")]
    public bool createPolygonCollider = true;

    [Tooltip("Set the generated collider as a trigger.")]
    public bool isTrigger = false;

    [Tooltip("Optional 2D Physics Material to assign to generated colliders.")]
    public PhysicsMaterial2D physicsMaterial2D;

    // References to other components
    private convex_hull convexHull;
    private clustering clusteringScript;
    private object_manager objectManager;

    // Platform pooling
    private readonly List<GameObject> platformPool = new List<GameObject>();
    private readonly List<Mesh> meshPool = new List<Mesh>();
    private readonly List<PolygonCollider2D> colliderPool = new List<PolygonCollider2D>();
    private readonly List<MeshRenderer> rendererPool = new List<MeshRenderer>();
    private readonly List<MeshFilter> filterPool = new List<MeshFilter>();

    private Material sharedShadowMaterial;
    private MaterialPropertyBlock propertyBlock;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int FallbackColorId = Shader.PropertyToID("_Color");
    private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
    private static readonly int SoftnessPowerId = Shader.PropertyToID("_SoftnessPower");

    void Start()
    {
        convexHull = GetComponent<convex_hull>();
        clusteringScript = GetComponent<clustering>();
        objectManager = GetComponent<object_manager>();

        ResolveWallReference();
        InitializeMaterial();

        propertyBlock = new MaterialPropertyBlock();
    }

    void Update()
    {
        if (wall == null)
        {
            ResolveWallReference();
            if (wall == null) return;
        }

        if (convexHull == null || convexHull.hulls == null)
        {
            DeactivateAllPlatforms();
            return;
        }

        if (sharedShadowMaterial == null)
        {
            InitializeMaterial();
        }

        // Dynamically compute the maximum possible distance between the light and the wall
        float effectiveMaxDist = maxDistance;
        if (autoAdjustDistanceRange && objectManager != null && objectManager.lights != null && objectManager.lights.Count > 0 && wall != null)
        {
            float maxLightDist = 0f;
            Vector3 wallPos = wall.transform.position;
            for (int l = 0; l < objectManager.lights.Count; l++)
            {
                Light lit = objectManager.lights[l];
                if (lit != null)
                {
                    float d = Vector3.Distance(lit.transform.position, wallPos);
                    if (d > maxLightDist) maxLightDist = d;
                }
            }
            if (maxLightDist > minDistance + 0.5f)
            {
                effectiveMaxDist = maxLightDist;
            }
        }

        List<List<Vector2>> hulls = convexHull.hulls;
        List<float> distances = convexHull.hull_distances;

        int activeCount = 0;

        for (int i = 0; i < hulls.Count; i++)
        {
            List<Vector2> hull = hulls[i];
            if (hull == null || hull.Count < 3)
                continue;

            // Make a clean copy and strip duplicate closing vertex if present
            List<Vector2> pathPoints = new List<Vector2>(hull);
            if (pathPoints.Count > 1 && pathPoints[0] == pathPoints[pathPoints.Count - 1])
            {
                pathPoints.RemoveAt(pathPoints.Count - 1);
            }

            if (pathPoints.Count < 3)
                continue;

            // Determine average distance for this hull
            float avgDist = 0f;
            if (distances != null && i < distances.Count)
            {
                avgDist = distances[i];
            }
            else if (clusteringScript != null && clusteringScript.avg_dist != null && i < clusteringScript.avg_dist.Count)
            {
                avgDist = clusteringScript.avg_dist[i];
            }

            // Calculate normalized distance factor (0 = close/sharp, 1 = far/soft)
            float normalizedDist = Mathf.Clamp01(Mathf.InverseLerp(minDistance, effectiveMaxDist, avgDist));
            float alpha = Mathf.Lerp(maxAlpha, minAlpha, normalizedDist);
            float edgeSoftness = Mathf.Lerp(minEdgeSoftness, maxEdgeSoftness, normalizedDist);

            // Rent or create pooled platform GameObject
            GameObject platform = GetOrCreatePlatform(activeCount);
            bool isPlane = wall != null && wall.sharedMesh != null && wall.sharedMesh.bounds.size.y < wall.sharedMesh.bounds.size.z;
            platform.transform.localRotation = isPlane ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
            platform.transform.localPosition = Vector3.zero;
            platform.transform.localScale = Vector3.one;

            MeshFilter mf = filterPool[activeCount];
            MeshRenderer mr = rendererPool[activeCount];
            PolygonCollider2D polyCol = colliderPool[activeCount];
            Mesh mesh = meshPool[activeCount];

            // 1. Update Collider
            if (createPolygonCollider && polyCol != null)
            {
                polyCol.enabled = true;
                polyCol.isTrigger = isTrigger;
                polyCol.sharedMaterial = physicsMaterial2D;
                polyCol.pathCount = 1;
                polyCol.SetPath(0, pathPoints.ToArray());
            }
            else if (polyCol != null)
            {
                polyCol.enabled = false;
            }

            // 2. Generate Visual Mesh with Edge Softening
            GeneratePlatformVisualMesh(mesh, pathPoints, edgeSoftness, zOffset, alpha);
            mf.sharedMesh = mesh;

            // 3. Set Per-Platform Shader Properties via MaterialPropertyBlock
            mr.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, shadowColor);
            propertyBlock.SetColor(FallbackColorId, new Color(shadowColor.r, shadowColor.g, shadowColor.b, shadowColor.a * alpha));
            propertyBlock.SetFloat(AlphaId, alpha);
            propertyBlock.SetFloat(SoftnessPowerId, softnessPower);
            mr.SetPropertyBlock(propertyBlock);

            platform.SetActive(true);
            activeCount++;
        }

        // Deactivate unused pooled platforms
        for (int p = activeCount; p < platformPool.Count; p++)
        {
            if (platformPool[p].activeSelf)
            {
                platformPool[p].SetActive(false);
            }
        }
    }

    /// <summary>
    /// Builds a 2D mesh for the convex hull with an optional feathered soft edge skirt.
    /// </summary>
    public void GeneratePlatformVisualMesh(Mesh mesh, List<Vector2> points, float softness, float z, float alpha)
    {
        mesh.Clear();

        int n = points.Count;
        if (n < 3) return;

        // 1. Compute Centroid
        Vector2 centroid = Vector2.zero;
        for (int i = 0; i < n; i++)
        {
            centroid += points[i];
        }
        centroid /= n;

        // If softness is virtually zero, build a simple single-layer fan mesh
        if (softness <= 0.0001f)
        {
            Vector3[] verts = new Vector3[n + 1];
            Vector2[] uvs = new Vector2[n + 1];
            Color[] colors = new Color[n + 1];
            Vector3[] norms = new Vector3[n + 1];
            int[] tris = new int[n * 3];

            Color fullColor = new Color(shadowColor.r, shadowColor.g, shadowColor.b, 1.0f);

            // Center vertex
            verts[0] = new Vector3(centroid.x, centroid.y, z);
            uvs[0] = Vector2.zero;
            colors[0] = fullColor;
            norms[0] = -Vector3.forward;

            // Perimeter vertices
            for (int i = 0; i < n; i++)
            {
                verts[i + 1] = new Vector3(points[i].x, points[i].y, z);
                uvs[i + 1] = Vector2.zero;
                colors[i + 1] = fullColor;
                norms[i + 1] = -Vector3.forward;

                int next = (i + 1) % n;
                tris[i * 3 + 0] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = next + 1;
            }

            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.colors = colors;
            mesh.normals = norms;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return;
        }

        // 2. Compute edge normals and vertex miters for outward expansion
        Vector2[] edgeNormals = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            Vector2 edge = points[next] - points[i];
            Vector2 normal = new Vector2(edge.y, -edge.x);
            float len = normal.magnitude;
            if (len > 1e-5f)
            {
                normal /= len;
                // Ensure normal points outward from centroid
                Vector2 midPoint = (points[i] + points[next]) * 0.5f;
                if (Vector2.Dot(normal, midPoint - centroid) < 0f)
                {
                    normal = -normal;
                }
            }
            else
            {
                normal = Vector2.zero;
            }
            edgeNormals[i] = normal;
        }

        Vector2[] miters = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            int prev = (i - 1 + n) % n;
            Vector2 nPrev = edgeNormals[prev];
            Vector2 nCurr = edgeNormals[i];
            Vector2 miter = nPrev + nCurr;
            float miterLen = miter.magnitude;

            if (miterLen > 1e-5f)
            {
                miter /= miterLen;
                // Ensure miter points outward from centroid
                if (Vector2.Dot(miter, points[i] - centroid) < 0f)
                {
                    miter = -miter;
                }
                float dot = Vector2.Dot(miter, nCurr);
                float scale = dot > 0.2f ? 1f / dot : 2f;
                scale = Mathf.Min(scale, 2.0f); // prevent spikes on acute vertices
                miters[i] = miter * scale;
            }
            else
            {
                miters[i] = nCurr;
            }
        }

        // 3. Generate inner (solid) and outer (feathered) boundary vertices
        float halfSoftness = softness * 0.5f;

        Vector3[] vertices = new Vector3[1 + 2 * n];
        Vector2[] uvArray = new Vector2[1 + 2 * n];
        Color[] colorArray = new Color[1 + 2 * n];
        Vector3[] normalArray = new Vector3[1 + 2 * n];

        Color solidColor = new Color(shadowColor.r, shadowColor.g, shadowColor.b, 1.0f);
        Color transparentColor = new Color(shadowColor.r, shadowColor.g, shadowColor.b, 0f);

        // Index 0: Centroid
        vertices[0] = new Vector3(centroid.x, centroid.y, z);
        uvArray[0] = Vector2.zero;
        colorArray[0] = solidColor;
        normalArray[0] = -Vector3.forward;

        for (int i = 0; i < n; i++)
        {
            float distToCenter = Vector2.Distance(points[i], centroid);
            float innerOffset = Mathf.Min(halfSoftness, distToCenter * 0.5f);

            Vector2 innerPos = points[i] - miters[i] * innerOffset;
            Vector2 outerPos = points[i] + miters[i] * halfSoftness;

            // Inner vertex (Indices 1 to n)
            int innerIdx = 1 + i;
            vertices[innerIdx] = new Vector3(innerPos.x, innerPos.y, z);
            uvArray[innerIdx] = Vector2.zero; // core
            colorArray[innerIdx] = solidColor;
            normalArray[innerIdx] = -Vector3.forward;

            // Outer vertex (Indices n + 1 to 2n)
            int outerIdx = 1 + n + i;
            vertices[outerIdx] = new Vector3(outerPos.x, outerPos.y, z);
            uvArray[outerIdx] = new Vector2(1f, 0f); // outer boundary
            colorArray[outerIdx] = transparentColor;
            normalArray[outerIdx] = -Vector3.forward;
        }

        // 4. Construct Triangles
        // Core fan: n triangles. Skirt quads: n * 2 triangles. Total: 3 * n triangles.
        int[] triangles = new int[n * 3 + n * 6];
        int triIdx = 0;

        // Core fan
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            triangles[triIdx++] = 0;
            triangles[triIdx++] = 1 + i;
            triangles[triIdx++] = 1 + next;
        }

        // Skirt quads connecting inner and outer vertices
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            int i0 = 1 + i;
            int i1 = 1 + next;
            int o0 = 1 + n + i;
            int o1 = 1 + n + next;

            // Triangle 1
            triangles[triIdx++] = i0;
            triangles[triIdx++] = o0;
            triangles[triIdx++] = i1;

            // Triangle 2
            triangles[triIdx++] = o0;
            triangles[triIdx++] = o1;
            triangles[triIdx++] = i1;
        }

        mesh.vertices = vertices;
        mesh.uv = uvArray;
        mesh.colors = colorArray;
        mesh.normals = normalArray;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
    }

    private GameObject GetOrCreatePlatform(int index)
    {
        if (index < platformPool.Count)
        {
            return platformPool[index];
        }

        GameObject platform = new GameObject($"shadow_platform_{index}");
        platform.transform.SetParent(wall.transform, false);
        platform.transform.localPosition = Vector3.zero;
        bool isPlane = wall != null && wall.sharedMesh != null && wall.sharedMesh.bounds.size.y < wall.sharedMesh.bounds.size.z;
        platform.transform.localRotation = isPlane ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
        platform.transform.localScale = Vector3.one;

        MeshFilter mf = platform.AddComponent<MeshFilter>();
        MeshRenderer mr = platform.AddComponent<MeshRenderer>();
        PolygonCollider2D polyCol = platform.AddComponent<PolygonCollider2D>();

        mr.sharedMaterial = sharedShadowMaterial;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        Mesh mesh = new Mesh { name = $"ShadowMesh_{index}" };

        platformPool.Add(platform);
        meshPool.Add(mesh);
        filterPool.Add(mf);
        rendererPool.Add(mr);
        colliderPool.Add(polyCol);

        return platform;
    }

    private void InitializeMaterial()
    {
        if (customMaterial != null)
        {
            sharedShadowMaterial = customMaterial;
            return;
        }

        if (shadowShader == null)
        {
            shadowShader = Shader.Find("Custom/ShadowPlatform");
        }

        if (shadowShader == null)
        {
            // Fallback to standard URP / Unlit if Custom/ShadowPlatform is not loaded
            shadowShader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (shadowShader == null)
        {
            shadowShader = Shader.Find("Sprites/Default");
        }

        if (shadowShader == null)
        {
            Debug.LogError("[collider_n_shade] No compatible shadow shader was found; live shadow visuals are disabled.", this);
            return;
        }

        sharedShadowMaterial = new Material(shadowShader)
        {
            name = "M_ShadowPlatform_Shared",
            enableInstancing = true
        };
        sharedShadowMaterial.SetColor(BaseColorId, shadowColor);
    }

    private void ResolveWallReference()
    {
        if (wall != null) return;

        if (objectManager == null)
        {
            objectManager = GetComponent<object_manager>();
        }

        if (objectManager != null && objectManager.wall != null)
        {
            wall = objectManager.wall;
            return;
        }

        GameObject wallObj = GameObject.Find("wall");
        if (wallObj != null)
        {
            wall = wallObj.GetComponent<MeshFilter>();
        }
    }

    private void DeactivateAllPlatforms()
    {
        for (int i = 0; i < platformPool.Count; i++)
        {
            if (platformPool[i] != null && platformPool[i].activeSelf)
            {
                platformPool[i].SetActive(false);
            }
        }
    }

    void OnDestroy()
    {
        for (int i = 0; i < platformPool.Count; i++)
        {
            if (platformPool[i] != null)
            {
                Destroy(platformPool[i]);
            }
            if (meshPool[i] != null)
            {
                Destroy(meshPool[i]);
            }
        }
        platformPool.Clear();
        meshPool.Clear();
        filterPool.Clear();
        rendererPool.Clear();
        colliderPool.Clear();
    }
}
