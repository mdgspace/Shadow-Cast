using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bakes Unity's default real-time shadows on a target wall into 2D colliders.
/// - Crisp, hard shadows become solid PolygonCollider2D platforms.
/// - Softer, diffuse shadows become trigger colliders with ShadowQuicksandEffector (buoyancy quicksand).
/// </summary>
[DisallowMultipleComponent]
public class ShadowWallBaker : MonoBehaviour
{
    [Header("Automatic Layer Discovery")]
    [Tooltip("Every active MeshFilter on this layer is treated as a wall. The first valid wall is used as the projection surface.")]
    public LayerMask wallLayer = 1 << 6;

    [Tooltip("Every Renderer on this layer is treated as a shadow caster.")]
    public LayerMask shadowCasterLayer = 1 << 7;

    [Header("Target Wall (Optional Override)")]
    [Tooltip("Optional compatibility override. Leave empty to discover the wall from wallLayer.")]
    public MeshFilter wall;

    [Tooltip("Optional camera override. The default dedicated snapshot camera is safer and does not modify the gameplay camera.")]
    public Camera snapshotCamera;

    [Tooltip("Use a temporary orthographic camera for baking. Keep enabled so the Main Camera is never modified.")]
    public bool useDedicatedSnapshotCamera = true;

    [HideInInspector]
    public LayerMask captureCullingMask = (1 << 6) | (1 << 7);

    [Tooltip("Distance in meters in front of the wall to position the snapshot camera.")]
    public float cameraDistance = 2.0f;

    [Tooltip("Optional neutral untextured white material to swap onto wall during snapshot for maximum shadow contrast.")]
    public Material neutralWallMaterial;

    [Tooltip("Enable only when the selected neutral material is a lit, shadow-receiving material. Unlit materials produce a white image with no shadow information.")]
    public bool useNeutralWallMaterial = false;

    [Header("Bake Snapshot Settings")]
    [Tooltip("Resolution of the greyscale snapshot texture.")]
    public Vector2Int snapshotResolution = new Vector2Int(512, 512);

    [Tooltip("Whether to stretch contrast so lit wall = 0.0 and deepest shadow = 1.0.")]
    public bool autoNormalizeContrast = true;

    [Header("Shadow Thresholds")]
    [Range(0.1f, 0.95f)]
    [Tooltip("Shadow intensity required for a SOLID crisp platform (0..1).")]
    public float crispThreshold = 0.65f;

    [Range(0.01f, 0.8f)]
    [Tooltip("Shadow intensity required for a SOFT quicksand platform (0..1). Must be less than crispThreshold.")]
    public float softThreshold = 0.20f;

    [Tooltip("Minimum pixel area to filter out noise, specks, and tiny shadow fragments.")]
    public float minPixelArea = 25f;

    [Range(0.001f, 0.05f)]
    [Tooltip("Tolerance for Ramer-Douglas-Peucker polygon simplification in UV space.")]
    public float simplificationTolerance = 0.006f;

    [Header("Quicksand Physics Tuning")]
    [Tooltip("Downward sink speed for slightly soft shadows (m/s).")]
    public float minSinkSpeed = 0.35f;

    [Tooltip("Downward sink speed for highly diffuse shadows (m/s).")]
    public float maxSinkSpeed = 3.5f;

    [Tooltip("Horizontal movement damping inside quicksand.")]
    public float horizontalViscosity = 5.0f;

    [Tooltip("Optional 2D Physics Material for solid platforms.")]
    public PhysicsMaterial2D solidPhysicsMaterial;

    [Tooltip("Whether to also configure Unity's built-in BuoyancyEffector2D.")]
    public bool useNativeBuoyancyEffector = false;

    [Header("Visual Meshes")]
    [Tooltip("Whether to generate visual meshes on the baked platforms for player visibility.")]
    public bool generateVisualMeshes = true;

    [Tooltip("Color tint for crisp solid shadow platforms.")]
    public Color crispVisualColor = new Color(0.1f, 0.1f, 0.1f, 0.95f);

    [Tooltip("Color tint for soft diffuse quicksand shadow platforms.")]
    public Color softVisualColor = new Color(0.4f, 0.25f, 0.6f, 0.65f);

    [Tooltip("Slight Z-offset in front of the wall to eliminate Z-fighting.")]
    public float zOffset = -0.003f;

    [Header("In-Game / Debug UI")]
    [Tooltip("Draw a quick 'Bake Shadows' button in the Game view GUI.")]
    public bool showOnGUIBakeButton = true;

    [Header("Bake Results (Read-Only)")]
    public int bakedCrispCount = 0;
    public int bakedSoftCount = 0;
    public float lastBakeDurationMs = 0f;
    [NonSerialized] public Texture2D lastSnapshot;

    // Root containers for organized hierarchy
    private Transform bakedPlatformsRoot;
    private readonly List<GameObject> activeCrispPlatforms = new List<GameObject>();
    private readonly List<GameObject> activeSoftPlatforms = new List<GameObject>();

    private Material runtimeCrispMat;
    private Material runtimeSoftMat;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int FallbackColorId = Shader.PropertyToID("_Color");

    private void Start()
    {
        ResolveWallReference();
        InitializeMaterials();
    }

    private void ResolveWallReference()
    {
        // Layer-driven discovery is the normal path. This keeps the baker independent
        // from scene object names and means new casters work automatically when placed
        // on the ShadowCaster layer.
        captureCullingMask = wallLayer | shadowCasterLayer;

        if (wall == null || !IsOnLayerMask(wall.gameObject.layer, wallLayer) || wall.sharedMesh == null)
        {
            wall = FindWallOnLayer();
        }

        if (wall == null)
        {
            // Compatibility fallback for scenes created before layer discovery.
            wall = GetComponent<MeshFilter>();
        }

        if (wall == null)
        {
            // Last compatibility fallback for older scenes using a named wall.
            GameObject wallObj = GameObject.Find("wall");
            if (wallObj == null) wallObj = GameObject.Find("Wall");
            if (wallObj != null)
            {
                wall = wallObj.GetComponent<MeshFilter>();
            }
        }
    }

    private MeshFilter FindWallOnLayer()
    {
        MeshFilter[] meshFilters = FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude);
        MeshFilter best = null;
        float bestArea = 0f;

        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter candidate = meshFilters[i];
            if (candidate == null || candidate.sharedMesh == null || !candidate.gameObject.activeInHierarchy)
                continue;
            if (!IsOnLayerMask(candidate.gameObject.layer, wallLayer))
                continue;

            Bounds bounds = candidate.sharedMesh.bounds;
            Vector3 scaledSize = Vector3.Scale(bounds.size, candidate.transform.lossyScale);
            float area = Mathf.Max(scaledSize.x * scaledSize.y, Mathf.Max(scaledSize.x * scaledSize.z, scaledSize.y * scaledSize.z));
            if (best == null || area > bestArea)
            {
                best = candidate;
                bestArea = area;
            }
        }

        return best;
    }

    private static bool IsOnLayerMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }

    private void InitializeMaterials()
    {
        Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlitShader == null) unlitShader = Shader.Find("Sprites/Default");
        if (unlitShader == null) unlitShader = Shader.Find("Unlit/Color");

        if (unlitShader == null)
        {
            Debug.LogError("[ShadowWallBaker] No compatible unlit shader was found. Visual platform meshes will be disabled.", this);
            return;
        }

        if (runtimeCrispMat == null)
        {
            runtimeCrispMat = new Material(unlitShader);
            SetMaterialColor(runtimeCrispMat, crispVisualColor);
        }

        if (runtimeSoftMat == null)
        {
            runtimeSoftMat = new Material(unlitShader);
            SetMaterialColor(runtimeSoftMat, softVisualColor);
        }
    }

    private void SetMaterialColor(Material mat, Color color)
    {
        if (mat.HasProperty(BaseColorId))
            mat.SetColor(BaseColorId, color);
        else if (mat.HasProperty(FallbackColorId))
            mat.SetColor(FallbackColorId, color);
        mat.color = color;

        // Enable transparency for alpha < 1
        if (color.a < 1.0f)
        {
            mat.SetFloat("_Surface", 1); // URP Transparent surface
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.renderQueue = 3000;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
    }

    /// <summary>
    /// Core bake function. Captures greyscale snapshot of the wall and generates 2D colliders.
    /// Can be invoked via Inspector button, ContextMenu, UI Button, or C# script.
    /// </summary>
    [ContextMenu("Bake Shadows")]
    public void Bake()
    {
        float startTime = Time.realtimeSinceStartup;

        ResolveWallReference();
        if (wall == null || wall.sharedMesh == null)
        {
            Debug.LogError("[ShadowWallBaker] Cannot bake: Target wall MeshFilter is missing or has no mesh assigned!", this);
            return;
        }

        InitializeMaterials();

        // 1. Take greyscale snapshot using snapshot camera
        Texture2D snapshot = CaptureWallSnapshot();
        if (snapshot == null)
        {
            Debug.LogError("[ShadowWallBaker] Snapshot capture failed!", this);
            return;
        }

        ReplaceSnapshot(snapshot);

        // 2. Build intensity grid
        int w = snapshot.width;
        int h = snapshot.height;
        float[,] intensityGrid = ShadowContourExtractor.BuildShadowIntensityGrid(snapshot, autoNormalizeContrast);

        // 3. Extract contours
        Bounds wallBounds = wall.sharedMesh.bounds;
        bool isPlaneMesh = wallBounds.size.y < wallBounds.size.z;

        ShadowContourExtractor.ExtractAllShadowContours(
            intensityGrid,
            w,
            h,
            crispThreshold,
            softThreshold,
            minPixelArea,
            simplificationTolerance,
            wallBounds,
            isPlaneMesh,
            out List<ShadowContourExtractor.ContourData> crispContours,
            out List<ShadowContourExtractor.ContourData> softContours);

        // 4. Clear old platforms
        ClearBakedPlatforms();

        // 5. Instantiate crisp solid platforms
        EnsureRootContainer();
        for (int i = 0; i < crispContours.Count; i++)
        {
            CreateCrispPlatform(crispContours[i], i, isPlaneMesh);
        }

        // 6. Instantiate soft quicksand platforms
        for (int i = 0; i < softContours.Count; i++)
        {
            CreateSoftPlatform(softContours[i], i, isPlaneMesh);
        }

        bakedCrispCount = activeCrispPlatforms.Count;
        bakedSoftCount = activeSoftPlatforms.Count;
        lastBakeDurationMs = (Time.realtimeSinceStartup - startTime) * 1000f;

        Debug.Log($"[ShadowWallBaker] Bake Complete in {lastBakeDurationMs:F1}ms! Generated: {bakedCrispCount} Crisp Solid Platforms, {bakedSoftCount} Soft Quicksand Platforms.");
    }

    /// <summary>
    /// Renders an orthographic snapshot of the wall into a Texture2D.
    /// </summary>
    public Texture2D CaptureWallSnapshot()
    {
        if (wall == null || wall.sharedMesh == null)
        {
            Debug.LogError("[ShadowWallBaker] Cannot capture a snapshot without a wall mesh.", this);
            return null;
        }

        Camera cam = useDedicatedSnapshotCamera ? null : snapshotCamera;
        bool temporaryCamera = false;
        RenderTexture rt = null;
        RenderTexture previousActive = RenderTexture.active;
        Renderer wallRenderer = wall.GetComponent<Renderer>();
        Material[] originalMaterials = null;
        bool rootWasActive = bakedPlatformsRoot != null && bakedPlatformsRoot.gameObject.activeSelf;
        Texture2D snapshot = null;

        // Capture the assigned camera state. The Main Camera is commonly reused as
        // the snapshot camera, so every property changed below must be restored.
        bool originalCameraEnabled = false;
        RenderTexture originalTargetTexture = null;
        CameraClearFlags originalClearFlags = CameraClearFlags.Skybox;
        Color originalBackgroundColor = Color.black;
        bool originalOrthographic = false;
        float originalOrthographicSize = 0f;
        float originalAspect = 0f;
        float originalNearClipPlane = 0f;
        float originalFarClipPlane = 0f;
        int originalCullingMask = 0;
        Vector3 originalCameraPosition = Vector3.zero;
        Quaternion originalCameraRotation = Quaternion.identity;

        // Determine wall dimensions and orientation
        Bounds bounds = wall.sharedMesh.bounds;
        bool isPlane = bounds.size.y < bounds.size.z;

        float wallWidth = bounds.size.x * wall.transform.lossyScale.x;
        float wallHeight = (isPlane ? bounds.size.z : bounds.size.y) * (isPlane ? wall.transform.lossyScale.z : wall.transform.lossyScale.y);

        Vector3 wallCenterLocal = bounds.center;
        Vector3 wallCenterWorld = wall.transform.TransformPoint(wallCenterLocal);

        // Compute wall front normal and up vector
        Vector3 wallNormalWorld;
        Vector3 wallUpWorld;

        if (isPlane)
        {
            wallNormalWorld = wall.transform.up;
            wallUpWorld = wall.transform.forward;
        }
        else
        {
            wallNormalWorld = -wall.transform.forward;
            wallUpWorld = wall.transform.up;
        }

        // Create a clean dedicated snapshot camera by default. Reusing the gameplay
        // camera can change its projection and leave the Game view with no renderer.
        if (cam == null)
        {
            temporaryCamera = true;
            GameObject camObj = new GameObject("TemporaryShadowSnapshotCamera");
            cam = camObj.AddComponent<Camera>();
            cam.hideFlags = HideFlags.HideAndDontSave;
        }

        if (!temporaryCamera)
        {
            originalCameraEnabled = cam.enabled;
            originalTargetTexture = cam.targetTexture;
            originalClearFlags = cam.clearFlags;
            originalBackgroundColor = cam.backgroundColor;
            originalOrthographic = cam.orthographic;
            originalOrthographicSize = cam.orthographicSize;
            originalAspect = cam.aspect;
            originalNearClipPlane = cam.nearClipPlane;
            originalFarClipPlane = cam.farClipPlane;
            originalCullingMask = cam.cullingMask;
            originalCameraPosition = cam.transform.position;
            originalCameraRotation = cam.transform.rotation;
        }

        // Configure orthographic camera to tightly frame the wall surface
        cam.enabled = false;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.white;
        cam.orthographic = true;
        cam.orthographicSize = Mathf.Max(0.1f, wallHeight * 0.5f);
        cam.aspect = Mathf.Max(0.01f, wallWidth / Mathf.Max(0.01f, wallHeight));
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = Mathf.Max(10f, cameraDistance * 2.5f);
        cam.cullingMask = captureCullingMask;

        // Position camera in front of wall looking squarely at center
        cam.transform.position = wallCenterWorld + wallNormalWorld * cameraDistance;
        cam.transform.rotation = Quaternion.LookRotation(-wallNormalWorld, wallUpWorld);

        // Render to temporary RenderTexture — match wall's aspect ratio
        float wallAspect = Mathf.Max(0.01f, wallWidth / Mathf.Max(0.01f, wallHeight));
        int resY = Mathf.Clamp(snapshotResolution.y, 64, 2048);
        int resX = Mathf.Clamp(Mathf.RoundToInt(resY * wallAspect), 64, 2048);
        rt = RenderTexture.GetTemporary(resX, resY, 24, RenderTextureFormat.ARGB32);
        rt.filterMode = FilterMode.Bilinear;

        cam.targetTexture = rt;

        // Optional: temporarily apply clean white shadow-receiving material to wall
        // Previously baked visual meshes must not cast into the new bake.
        if (bakedPlatformsRoot != null)
        {
            bakedPlatformsRoot.gameObject.SetActive(false);
        }

        if (useNeutralWallMaterial && neutralWallMaterial != null && wallRenderer != null)
        {
            originalMaterials = wallRenderer.sharedMaterials;
            Material[] neutralMaterials = new Material[Mathf.Max(1, originalMaterials.Length)];
            for (int i = 0; i < neutralMaterials.Length; i++)
            {
                neutralMaterials[i] = neutralWallMaterial;
            }
            wallRenderer.sharedMaterials = neutralMaterials;
        }

        try
        {
            cam.Render();

            // Readback to CPU Texture2D.
            RenderTexture.active = rt;
            snapshot = new Texture2D(resX, resY, TextureFormat.RGB24, false);
            snapshot.ReadPixels(new Rect(0, 0, resX, resY), 0, 0);
            snapshot.Apply(false, false);
            return snapshot;
        }
        catch (Exception ex)
        {
            Debug.LogException(ex, this);
            if (snapshot != null)
            {
                if (Application.isPlaying)
                    Destroy(snapshot);
                else
                    DestroyImmediate(snapshot);
                snapshot = null;
            }
            return null;
        }
        finally
        {
            if (wallRenderer != null && originalMaterials != null)
            {
                wallRenderer.sharedMaterials = originalMaterials;
            }

            if (bakedPlatformsRoot != null)
            {
                bakedPlatformsRoot.gameObject.SetActive(rootWasActive);
            }

            RenderTexture.active = previousActive;
            if (cam != null)
            {
                if (temporaryCamera)
                {
                    cam.targetTexture = null;
                }
                else
                {
                    cam.enabled = originalCameraEnabled;
                    cam.targetTexture = originalTargetTexture;
                    cam.clearFlags = originalClearFlags;
                    cam.backgroundColor = originalBackgroundColor;
                    cam.orthographic = originalOrthographic;
                    cam.orthographicSize = originalOrthographicSize;
                    cam.aspect = originalAspect;
                    cam.nearClipPlane = originalNearClipPlane;
                    cam.farClipPlane = originalFarClipPlane;
                    cam.cullingMask = originalCullingMask;
                    cam.transform.SetPositionAndRotation(originalCameraPosition, originalCameraRotation);
                }
            }
            if (rt != null)
            {
                RenderTexture.ReleaseTemporary(rt);
            }

            if (temporaryCamera && cam != null)
            {
                if (Application.isPlaying)
                    Destroy(cam.gameObject);
                else
                    DestroyImmediate(cam.gameObject);
            }
        }
    }

    private void ReplaceSnapshot(Texture2D snapshot)
    {
        if (lastSnapshot != null && lastSnapshot != snapshot)
        {
            if (Application.isPlaying)
                Destroy(lastSnapshot);
            else
                DestroyImmediate(lastSnapshot);
        }

        lastSnapshot = snapshot;
    }

    private void EnsureRootContainer()
    {
        if (bakedPlatformsRoot == null)
        {
            Transform existing = wall.transform.Find("BakedShadowPlatforms");
            if (existing != null)
            {
                bakedPlatformsRoot = existing;
            }
            else
            {
                GameObject rootObj = new GameObject("BakedShadowPlatforms");
                rootObj.transform.SetParent(wall.transform, false);
                rootObj.transform.localPosition = Vector3.zero;
                rootObj.transform.localRotation = Quaternion.identity;
                rootObj.transform.localScale = Vector3.one;
                bakedPlatformsRoot = rootObj.transform;
            }
        }
    }

    private void CreateCrispPlatform(ShadowContourExtractor.ContourData contour, int index, bool isPlane)
    {
        GameObject go = new GameObject($"Platform_Crisp_{index}");
        go.transform.SetParent(bakedPlatformsRoot, false);
        go.transform.localRotation = isPlane ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
        go.transform.localPosition = Vector3.forward * zOffset;
        go.transform.localScale = Vector3.one;

        // 1. Solid Polygon Collider
        PolygonCollider2D poly = go.AddComponent<PolygonCollider2D>();
        poly.isTrigger = false;
        poly.sharedMaterial = solidPhysicsMaterial;
        poly.pathCount = 1;
        poly.SetPath(0, contour.wallSpacePoints.ToArray());

        // 2. Solid platform marker
        ShadowSolidPlatform solid = go.AddComponent<ShadowSolidPlatform>();
        solid.platformId = index;
        solid.shadowIntensity = contour.averageIntensity;
        solid.polygonCollider = poly;

        // 3. Optional Visual Mesh
        if (generateVisualMeshes)
        {
            MeshFilter mf = go.AddComponent<MeshFilter>();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = Create2DPolygonMesh(contour.wallSpacePoints, crispVisualColor);
            mr.sharedMaterial = runtimeCrispMat;
        }

        activeCrispPlatforms.Add(go);
    }

    private void CreateSoftPlatform(ShadowContourExtractor.ContourData contour, int index, bool isPlane)
    {
        GameObject go = new GameObject($"Platform_Soft_{index}");
        go.transform.SetParent(bakedPlatformsRoot, false);
        go.transform.localRotation = isPlane ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
        go.transform.localPosition = Vector3.forward * (zOffset * 0.5f);
        go.transform.localScale = Vector3.one;

        // 1. Quicksand Trigger Polygon Collider
        PolygonCollider2D poly = go.AddComponent<PolygonCollider2D>();
        poly.isTrigger = true;
        poly.pathCount = 1;
        poly.SetPath(0, contour.wallSpacePoints.ToArray());

        // 2. Quicksand Effector with Buoyancy
        ShadowQuicksandEffector effector = go.AddComponent<ShadowQuicksandEffector>();
        effector.platformId = index;
        effector.minSinkSpeed = minSinkSpeed;
        effector.maxSinkSpeed = maxSinkSpeed;
        effector.horizontalViscosity = horizontalViscosity;
        effector.useNativeBuoyancyEffector = useNativeBuoyancyEffector;
        effector.SetSoftness(contour.softness);

        // 3. Optional Visual Mesh (Tints with softness)
        if (generateVisualMeshes)
        {
            MeshFilter mf = go.AddComponent<MeshFilter>();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            Color tintedColor = Color.Lerp(crispVisualColor, softVisualColor, contour.softness);
            tintedColor.a = Mathf.Lerp(0.85f, 0.45f, contour.softness);

            mf.sharedMesh = Create2DPolygonMesh(contour.wallSpacePoints, tintedColor);
            mr.sharedMaterial = runtimeSoftMat;
        }

        activeSoftPlatforms.Add(go);
    }

    /// <summary>
    /// Generates a triangulated 2D Mesh for the given wall polygon points using ear clipping.
    /// </summary>
    private Mesh Create2DPolygonMesh(List<Vector2> points, Color color)
    {
        Mesh mesh = new Mesh();
        int n = points.Count;
        if (n < 3) return mesh;

        Vector3[] vertices = new Vector3[n];
        Vector2[] uvs = new Vector2[n];
        Color[] colors = new Color[n];

        // Bounds for UVs
        Vector2 min = points[0], max = points[0];
        for (int i = 0; i < n; i++)
        {
            min = Vector2.Min(min, points[i]);
            max = Vector2.Max(max, points[i]);
        }
        Vector2 size = max - min;
        if (size.x < 1e-4f) size.x = 1f;
        if (size.y < 1e-4f) size.y = 1f;

        for (int i = 0; i < n; i++)
        {
            vertices[i] = new Vector3(points[i].x, points[i].y, 0f);
            uvs[i] = new Vector2((points[i].x - min.x) / size.x, (points[i].y - min.y) / size.y);
            colors[i] = color;
        }

        int[] triangles = EarClipTriangulate(points);

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.colors = colors;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }

    /// <summary>
    /// Simple, robust Ear-Clipping triangulation for 2D simple polygons.
    /// </summary>
    private int[] EarClipTriangulate(List<Vector2> polygon)
    {
        int n = polygon.Count;
        if (n < 3) return new int[0];

        List<int> indices = new List<int>(n);
        // Ensure counter-clockwise orientation
        float area = ShadowContourExtractor.ComputePolygonArea(polygon);
        bool ccw = IsCounterClockwise(polygon);

        for (int i = 0; i < n; i++)
        {
            indices.Add(ccw ? i : (n - 1 - i));
        }

        List<int> triangleList = new List<int>((n - 2) * 3);
        int iterations = 0;
        int maxIterations = n * 4;

        while (indices.Count > 3 && iterations < maxIterations)
        {
            iterations++;
            bool earFound = false;

            for (int i = 0; i < indices.Count; i++)
            {
                int prev = indices[(i - 1 + indices.Count) % indices.Count];
                int curr = indices[i];
                int next = indices[(i + 1) % indices.Count];

                Vector2 a = polygon[prev];
                Vector2 b = polygon[curr];
                Vector2 c = polygon[next];

                // Check convexity: cross product z > 0 for ccw
                float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
                if (cross <= 0f) continue;

                // Check if any other vertex is inside triangle (a, b, c)
                bool hasPointInside = false;
                for (int j = 0; j < indices.Count; j++)
                {
                    int testIdx = indices[j];
                    if (testIdx == prev || testIdx == curr || testIdx == next) continue;

                    if (IsPointInTriangle(polygon[testIdx], a, b, c))
                    {
                        hasPointInside = true;
                        break;
                    }
                }

                if (!hasPointInside)
                {
                    triangleList.Add(prev);
                    triangleList.Add(curr);
                    triangleList.Add(next);
                    indices.RemoveAt(i);
                    earFound = true;
                    break;
                }
            }

            if (!earFound)
            {
                // Fallback: fan-triangulate remaining indices
                break;
            }
        }

        // Add remaining triangle or fan
        if (indices.Count >= 3)
        {
            for (int i = 1; i < indices.Count - 1; i++)
            {
                triangleList.Add(indices[0]);
                triangleList.Add(indices[i]);
                triangleList.Add(indices[i + 1]);
            }
        }

        return triangleList.ToArray();
    }

    private bool IsCounterClockwise(List<Vector2> pts)
    {
        float sum = 0f;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector2 p1 = pts[i];
            Vector2 p2 = pts[(i + 1) % pts.Count];
            sum += (p2.x - p1.x) * (p2.y + p1.y);
        }
        return sum < 0f;
    }

    private bool IsPointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Sign(p, a, b);
        float d2 = Sign(p, b, c);
        float d3 = Sign(p, c, a);

        bool hasNeg = (d1 < 0) || (d2 < 0) || (d3 < 0);
        bool hasPos = (d1 > 0) || (d2 > 0) || (d3 > 0);

        return !(hasNeg && hasPos);
    }

    private float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
    {
        return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
    }

    /// <summary>
    /// Clears and destroys all previously generated platforms.
    /// </summary>
    [ContextMenu("Clear Baked Platforms")]
    public void ClearBakedPlatforms()
    {
        if (bakedPlatformsRoot != null)
        {
            for (int i = bakedPlatformsRoot.childCount - 1; i >= 0; i--)
            {
                GameObject child = bakedPlatformsRoot.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
        }
        activeCrispPlatforms.Clear();
        activeSoftPlatforms.Clear();

        bakedCrispCount = 0;
        bakedSoftCount = 0;
    }

    private void OnDestroy()
    {
        if (lastSnapshot != null)
        {
            if (Application.isPlaying)
                Destroy(lastSnapshot);
            else
                DestroyImmediate(lastSnapshot);
            lastSnapshot = null;
        }

        if (runtimeCrispMat != null)
        {
            if (Application.isPlaying)
                Destroy(runtimeCrispMat);
            else
                DestroyImmediate(runtimeCrispMat);
            runtimeCrispMat = null;
        }

        if (runtimeSoftMat != null)
        {
            if (Application.isPlaying)
                Destroy(runtimeSoftMat);
            else
                DestroyImmediate(runtimeSoftMat);
            runtimeSoftMat = null;
        }
    }

    private void OnGUI()
    {
        if (!showOnGUIBakeButton) return;

        GUILayout.BeginArea(new Rect(15, 15, 200, 140));
        GUILayout.BeginVertical("box");

        GUI.color = Color.cyan;
        GUIStyle richLabel = new GUIStyle(GUI.skin.label) { richText = true };
        GUILayout.Label("<b>Shadow Parkour</b>", richLabel);
        GUI.color = Color.white;

        if (GUILayout.Button("⚡ Bake Shadows", GUILayout.Height(35)))
        {
            Bake();
        }

        if (GUILayout.Button("Clear Platforms", GUILayout.Height(25)))
        {
            ClearBakedPlatforms();
        }

        GUILayout.Label($"Crisp: {bakedCrispCount} | Soft: {bakedSoftCount}");

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }
}
