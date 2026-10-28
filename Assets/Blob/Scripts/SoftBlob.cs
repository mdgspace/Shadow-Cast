using UnityEngine;


[RequireComponent(typeof(Rigidbody2D), typeof(MeshFilter), typeof(MeshRenderer))]
public class SoftBlob : MonoBehaviour
{
    [Header("Shape")]
    [SerializeField] private int nodeCount = 14;
    [SerializeField] private float radius = 0.8f;
    [SerializeField] private float nodeRadius = 0.1f;
    [SerializeField] private float nodeMass = 0.2f;

    [Header("Softness")]
    [SerializeField] private float coreFrequency = 4f;
    [SerializeField] private float coreDamping = 0.3f;
    [SerializeField] private float neighborFrequency = 8f;
    [SerializeField] private float neighborDamping = 0.5f;

    [Header("Shape Memory")]
    [SerializeField] private float shapeStiffness = 15f;
    [SerializeField] private float shapeDamping = 4f;
    [SerializeField, Range(0f, 1f)] private float squeezeFactor = 0.15f;
    [SerializeField] private bool useOppositeBrace = false;
    [SerializeField] private float crossFrequency = 3f;
    [SerializeField] private float nodeFriction = 0.3f;
    [SerializeField] private float maxStretch = 3f;

    [Header("Look")]
    [SerializeField] private Texture2D fillTexture;
    [SerializeField] private Color fillColor = new Color(0.55f, 0.1f, 0.15f);
    [SerializeField] private Color outlineColor = new Color(0.25f, 0.03f, 0.06f);
    [SerializeField] private float outlineWidth = 0.08f;
    [SerializeField, Range(1, 8)] private int smoothing = 5;
    [SerializeField] private int sortingOrder = 0;

    [Header("Face")]
    [SerializeField] private Transform face;
    [SerializeField] private Vector2 faceOffset = new Vector2(0.2f, 0.1f);
    [SerializeField] private float faceSpring = 120f;
    [SerializeField] private float faceDrag = 8f;
    [SerializeField] private float lookAmount = 0.25f;
    [SerializeField] private float lookSpeedForMax = 6f;
    [SerializeField] private float lookSmoothing = 8f;
    [SerializeField] private bool lookVertical = true;

    Rigidbody2D core;
    Rigidbody2D[] nodes;
    Vector2[] restDirs;
    PhysicsMaterial2D nodeMat;
    Mesh mesh;
    Vector3[] verts;
    Vector2[] uvs;
    Vector3[] linePts;
    LineRenderer line;
    Vector2 faceVel;
    Vector2 look;

    public float Radius => radius;
    public Vector2 Centroid { get; private set; }

    int TotalPoints => nodeCount * smoothing;

    void Awake()
    {
        SetupCore();
        CreateNodes();
        CreateNeighborSprings();
        CreateCrossBraces();
        SetupFillMesh();
        SetupOutline();
        UpdateCentroid();
    }

    void FixedUpdate()
    {
        UpdateCentroid();
        for (int i = 0; i < nodeCount; i++)
        {
            ApplyShapeMemory(i);
        }
    }

    void LateUpdate()
    {
        UpdateCentroid();
        UpdateMeshAndOutline();
        UpdateFacePosition(Centroid);
    }

    void SetupCore()
    {
        core = GetComponent<Rigidbody2D>();
        core.freezeRotation = true;
        core.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    void CreateNodes()
    {
        nodes = new Rigidbody2D[nodeCount];
        restDirs = new Vector2[nodeCount];
        nodeMat = new PhysicsMaterial2D("BlobNode") { friction = nodeFriction, bounciness = 0f };
        for (int i = 0; i < nodeCount; i++)
        {
            float a = i * Mathf.PI * 2f / nodeCount;
            Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            restDirs[i] = dir;

            GameObject go = new GameObject("Node" + i);
            go.layer = gameObject.layer;
            go.transform.position = (Vector2)transform.position + dir * radius;

            Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
            rb.mass = nodeMass;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            CircleCollider2D cc = go.AddComponent<CircleCollider2D>();
            cc.radius = nodeRadius;
            cc.sharedMaterial = nodeMat;

            AddSpring(go, core, radius, coreFrequency, coreDamping);

            nodes[i] = rb;
        }
    }

    void CreateNeighborSprings()
    {
        float side = 2f * radius * Mathf.Sin(Mathf.PI / nodeCount);
        for (int i = 0; i < nodeCount; i++)
        {
            AddSpring(nodes[i].gameObject, nodes[(i + 1) % nodeCount], side, neighborFrequency, neighborDamping);
        }
    }

    void CreateCrossBraces()
    {
        int[] skips = useOppositeBrace ? new int[] { 2, nodeCount / 2 } : new int[] { 2 };
        foreach (int skip in skips)
        {
            float chord = 2f * radius * Mathf.Sin(Mathf.PI * skip / nodeCount);
            for (int i = 0; i < nodeCount; i++)
            {
                if (skip == nodeCount / 2 && i >= nodeCount / 2) continue;
                AddSpring(nodes[i].gameObject, nodes[(i + skip) % nodeCount], chord, crossFrequency, neighborDamping);
            }
        }
    }

    SpringJoint2D AddSpring(GameObject host, Rigidbody2D target, float distance, float frequency, float damping)
    {
        SpringJoint2D sj = host.AddComponent<SpringJoint2D>();
        sj.connectedBody = target;
        sj.autoConfigureDistance = false;
        sj.distance = distance;
        sj.anchor = Vector2.zero;
        sj.connectedAnchor = Vector2.zero;
        sj.frequency = frequency;
        sj.dampingRatio = damping;
        return sj;
    }

    void SetupFillMesh()
    {
        int total = TotalPoints;


        MeshRenderer mr = GetComponent<MeshRenderer>();
        Material mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = fillColor;
        if (fillTexture != null) mat.mainTexture = fillTexture;
        mr.material = mat;
        mr.sortingOrder = sortingOrder;

        mesh = new Mesh();
        mesh.MarkDynamic();
        verts = new Vector3[total + 1];
        uvs = new Vector2[total + 1];
        int[] tris = new int[total * 3];
        for (int i = 0; i < total; i++)
        {
            tris[i * 3] = total;
            tris[i * 3 + 1] = i;
            tris[i * 3 + 2] = (i + 1) % total;
        }
        mesh.vertices = verts;
        mesh.triangles = tris;
        GetComponent<MeshFilter>().mesh = mesh;
    }

    void SetupOutline()
    {
        int total = TotalPoints;

        GameObject lgo = new GameObject("Outline");
        lgo.transform.SetParent(transform, false);
        line = lgo.AddComponent<LineRenderer>();
        line.loop = true;
        line.useWorldSpace = true;
        line.positionCount = total;
        line.widthMultiplier = outlineWidth;
        line.numCornerVertices = 4;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.startColor = line.endColor = outlineColor;
        line.sortingOrder = sortingOrder + 1;
        linePts = new Vector3[total];
    }

    void UpdateCentroid()
    {
        Vector2 sum = Vector2.zero;
        foreach (Rigidbody2D n in nodes) sum += n.position;
        Centroid = sum / nodes.Length;
    }

    void ApplyShapeMemory(int i)
    {
        Rigidbody2D n = nodes[i];
        Vector2 target = core.position + restDirs[i] * radius;
        Vector2 err = target - n.position;


        if (err.magnitude > radius * maxStretch)
        {
            n.position = target;
            n.linearVelocity = core.linearVelocity;
            return;
        }

        Vector2 relVel = n.linearVelocity - core.linearVelocity;

        bool squeezed = n.IsTouchingLayers(~(1 << gameObject.layer));
        float k = squeezed ? squeezeFactor : 1f;
        n.AddForce((err * shapeStiffness * k - relVel * shapeDamping) * n.mass);
    }

    static Vector2 Spline(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
            + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    void UpdateMeshAndOutline()
    {
        int total = TotalPoints;
        int k = 0;
        Vector2 c = Centroid;
        for (int i = 0; i < nodeCount; i++)
        {
            Vector2 p0 = nodes[(i - 1 + nodeCount) % nodeCount].position;
            Vector2 p1 = nodes[i].position;
            Vector2 p2 = nodes[(i + 1) % nodeCount].position;
            Vector2 p3 = nodes[(i + 2) % nodeCount].position;
            for (int s = 0; s < smoothing; s++, k++)
            {
                Vector2 p = Spline(p0, p1, p2, p3, s / (float)smoothing);

                p += (p - c).normalized * nodeRadius;
                linePts[k] = p;
                verts[k] = transform.InverseTransformPoint(p);


                float ang = (i + s / (float)smoothing) * Mathf.PI * 2f / nodeCount;
                uvs[k] = new Vector2(0.5f + 0.5f * Mathf.Cos(ang), 0.5f + 0.5f * Mathf.Sin(ang));
            }
        }

        verts[total] = transform.InverseTransformPoint(c);
        uvs[total] = Vector2.one * 0.5f;
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.RecalculateBounds();
        line.SetPositions(linePts);
    }

    void UpdateFacePosition(Vector2 centroid)
    {
        if (face == null) return;

        Vector2 v = core.linearVelocity / lookSpeedForMax;
        if (!lookVertical) v.y = 0f;
        v = Vector2.ClampMagnitude(v, 1f);
        look = Vector2.Lerp(look, v, 1f - Mathf.Exp(-lookSmoothing * Time.deltaTime));

        Vector2 target = centroid + faceOffset + look * lookAmount;
        Vector2 pos = face.position;
        faceVel += (target - pos) * faceSpring * Time.deltaTime;
        faceVel -= faceVel * faceDrag * Time.deltaTime;
        face.position = new Vector3(pos.x + faceVel.x * Time.deltaTime,
                                   pos.y + faceVel.y * Time.deltaTime,
                                   face.position.z);
    }
}