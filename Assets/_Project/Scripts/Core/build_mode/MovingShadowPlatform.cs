using UnityEngine;
using UnityEngine.Rendering;

namespace ShadowCast.Core.BuildMode
{
    public enum MovingShadowType { TypeA_FixedAxis, TypeB_RotationDriven }
    public enum FixedShadowAxis { Horizontal, Vertical }

    /// <summary>
    /// Dedicated authoring component for an animated platform shadow. The source is a procedural
    /// cube-minus-triangle; its playable shadow is generated after the regular bake.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ArrowCutoutCube))]
    public sealed class MovingShadowPlatform : MonoBehaviour
    {
        [Header("Movement Type")]
        [SerializeField] private MovingShadowType movementType = MovingShadowType.TypeA_FixedAxis;
        [SerializeField] private FixedShadowAxis typeAFixedAxis = FixedShadowAxis.Horizontal;
        [SerializeField, Min(0.01f)] private float speed = 0.35f;
        [SerializeField, Min(0f)] private float edgePadding = 0.04f;

        [Header("Generated Shadow")]
        [SerializeField] private Vector2 shadowSize = new Vector2(0.42f, 0.32f);
        [SerializeField] private Vector2 arrowHoleSize = new Vector2(0.20f, 0.17f);
        [SerializeField] private Color typeAColor = new Color(0.05f, 0.58f, 0.95f, 1f);
        [SerializeField] private Color typeBColor = new Color(1f, 0.35f, 0.08f, 1f);
        [SerializeField] private bool useAutomaticTypeColor = true;

        private Transform wall;
        private GameObject generatedShadow;
        private Renderer[] sourceRenderers;
        private ShadowCastingMode[] originalShadowModes;
        private Vector3 endpointA;
        private Vector3 endpointB;
        private float journeyLength;
        private Material generatedMaterial;
        private Material sourceMaterial;
        private Mesh generatedMesh;

        public MovingShadowType MovementType => movementType;
        public bool HasGeneratedShadow => generatedShadow != null;

        private void OnEnable() => UpdateSourceAppearance();
        private void OnValidate() => UpdateSourceAppearance();

        public void PrepareForBake(Transform targetWall)
        {
            wall = targetWall;
            sourceRenderers = GetComponentsInChildren<Renderer>(true);
            originalShadowModes = new ShadowCastingMode[sourceRenderers.Length];
            for (int i = 0; i < sourceRenderers.Length; i++)
            {
                originalShadowModes[i] = sourceRenderers[i].shadowCastingMode;
                sourceRenderers[i].shadowCastingMode = ShadowCastingMode.Off;
            }
            ClearGeneratedShadow();
        }

        public bool CreateBakedShadow(Transform targetWall)
        {
            wall = targetWall;
            if (wall == null) return false;

            generatedShadow = new GameObject($"MovingShadow_{movementType}_{name}");
            generatedShadow.transform.SetParent(wall.Find("BakedShadowPlatforms") ?? wall, false);
            generatedShadow.transform.localRotation = LocalShadowRotation();
            generatedShadow.transform.localScale = Vector3.one;

            Vector3 localSource = wall.InverseTransformPoint(transform.position);
            localSource.z = -0.004f;
            CalculateEndpoints(localSource);
            generatedShadow.transform.localPosition = endpointA;
            journeyLength = Vector3.Distance(endpointA, endpointB);

            PolygonCollider2D collider = generatedShadow.AddComponent<PolygonCollider2D>();
            Vector2[][] paths = ArrowCutoutGeometry.CreateColliderPaths(shadowSize, arrowHoleSize);
            collider.pathCount = paths.Length;
            for (int i = 0; i < paths.Length; i++) collider.SetPath(i, paths[i]);

            Rigidbody2D body = generatedShadow.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;

            MeshFilter filter = generatedShadow.AddComponent<MeshFilter>();
            MeshRenderer renderer = generatedShadow.AddComponent<MeshRenderer>();
            generatedMesh = ArrowCutoutGeometry.CreateMesh(new Vector3(shadowSize.x, shadowSize.y, 0.002f), arrowHoleSize);
            filter.sharedMesh = generatedMesh;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader != null)
            {
                generatedMaterial = new Material(shader) { name = $"{movementType}_ShadowMaterial" };
                Color color = movementType == MovingShadowType.TypeA_FixedAxis ? typeAColor : typeBColor;
                if (generatedMaterial.HasProperty("_BaseColor")) generatedMaterial.SetColor("_BaseColor", color);
                if (generatedMaterial.HasProperty("_Color")) generatedMaterial.SetColor("_Color", color);
                renderer.sharedMaterial = generatedMaterial;
            }
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return true;
        }

        private void Update()
        {
            if (!Application.isPlaying) return;
            if (generatedShadow == null || journeyLength < 0.0001f) return;
            float duration = journeyLength / Mathf.Max(0.01f, speed);
            float t = Mathf.PingPong(Time.time / duration, 1f);
            generatedShadow.transform.localPosition = Vector3.Lerp(endpointA, endpointB, t);
        }

        public void RestoreBuildState()
        {
            if (sourceRenderers != null && originalShadowModes != null)
                for (int i = 0; i < sourceRenderers.Length && i < originalShadowModes.Length; i++)
                    if (sourceRenderers[i] != null) sourceRenderers[i].shadowCastingMode = originalShadowModes[i];
            ClearGeneratedShadow();
        }

        private Quaternion LocalShadowRotation()
        {
            if (movementType == MovingShadowType.TypeA_FixedAxis)
                return typeAFixedAxis == FixedShadowAxis.Horizontal ? Quaternion.identity : Quaternion.Euler(0f, 0f, 90f);
            Vector3 localRight = wall.InverseTransformDirection(transform.right);
            return Quaternion.Euler(0f, 0f, Mathf.Atan2(localRight.y, localRight.x) * Mathf.Rad2Deg);
        }

        private Vector2 MovementDirection()
        {
            if (movementType == MovingShadowType.TypeA_FixedAxis)
                return typeAFixedAxis == FixedShadowAxis.Horizontal ? Vector2.right : Vector2.up;
            Vector3 local = wall.InverseTransformDirection(transform.right);
            Vector2 direction = new Vector2(local.x, local.y);
            return direction.sqrMagnitude < 0.0001f ? Vector2.right : direction.normalized;
        }

        private void CalculateEndpoints(Vector3 origin)
        {
            MeshFilter wallMesh = wall.GetComponent<MeshFilter>();
            Bounds bounds = wallMesh != null && wallMesh.sharedMesh != null ? wallMesh.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
            Vector2 dir = MovementDirection();
            Vector2 half = shadowSize * 0.5f + Vector2.one * edgePadding;
            float minT = float.NegativeInfinity;
            float maxT = float.PositiveInfinity;
            ApplyAxisBounds(origin.x, dir.x, bounds.min.x + half.x, bounds.max.x - half.x, ref minT, ref maxT);
            ApplyAxisBounds(origin.y, dir.y, bounds.min.y + half.y, bounds.max.y - half.y, ref minT, ref maxT);
            if (float.IsInfinity(minT)) minT = 0f;
            if (float.IsInfinity(maxT)) maxT = 0f;
            endpointA = origin + new Vector3(dir.x, dir.y, 0f) * minT;
            endpointB = origin + new Vector3(dir.x, dir.y, 0f) * maxT;
        }

        private static void ApplyAxisBounds(float origin, float direction, float min, float max, ref float minT, ref float maxT)
        {
            if (Mathf.Abs(direction) < 0.0001f) return;
            float a = (min - origin) / direction;
            float b = (max - origin) / direction;
            if (a > b) { float swap = a; a = b; b = swap; }
            minT = Mathf.Max(minT, a);
            maxT = Mathf.Min(maxT, b);
        }

        private void ClearGeneratedShadow()
        {
            DestroyGenerated(generatedShadow);
            DestroyGenerated(generatedMesh);
            DestroyGenerated(generatedMaterial);
            generatedShadow = null;
            generatedMesh = null;
            generatedMaterial = null;
        }

        private void UpdateSourceAppearance()
        {
            if (!useAutomaticTypeColor) return;
            Renderer sourceRenderer = GetComponent<Renderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (sourceRenderer == null || shader == null) return;
            if (sourceMaterial == null)
            {
                sourceMaterial = new Material(shader) { name = $"{movementType}_SourcePreview", hideFlags = HideFlags.DontSave };
            }
            sourceRenderer.sharedMaterial = sourceMaterial;
            Color color = movementType == MovingShadowType.TypeA_FixedAxis ? typeAColor : typeBColor;
            if (sourceMaterial.HasProperty("_BaseColor")) sourceMaterial.SetColor("_BaseColor", color);
            if (sourceMaterial.HasProperty("_Color")) sourceMaterial.SetColor("_Color", color);
        }

        private static void DestroyGenerated(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target); else DestroyImmediate(target);
        }

        private void OnDisable()
        {
            RestoreBuildState();
            DestroyGenerated(sourceMaterial);
            sourceMaterial = null;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = movementType == MovingShadowType.TypeA_FixedAxis ? typeAColor : typeBColor;
            Vector3 direction = movementType == MovingShadowType.TypeA_FixedAxis
                ? (typeAFixedAxis == FixedShadowAxis.Horizontal ? transform.right : transform.up)
                : transform.right;
            Gizmos.DrawLine(transform.position - direction, transform.position + direction);
            Gizmos.DrawWireCube(transform.position, transform.TransformVector(new Vector3(shadowSize.x, shadowSize.y, 0.04f)));
        }
    }
}
