using UnityEngine;

namespace ShadowCast.Core.BuildMode
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ArrowCutoutCube : MonoBehaviour
    {
        [SerializeField] private Vector3 cubeSize = Vector3.one;
        [SerializeField] private Vector2 arrowHoleSize = new Vector2(0.48f, 0.42f);
        [SerializeField] private bool addMeshCollider = true;

        private Mesh generatedMesh;
        public Vector3 CubeSize => cubeSize;
        public Vector2 ArrowHoleSize => arrowHoleSize;

        private void OnEnable() => Rebuild();
        private void OnValidate() => Rebuild();

        [ContextMenu("Rebuild Boolean Cube")]
        public void Rebuild()
        {
            cubeSize = new Vector3(Mathf.Max(0.1f, cubeSize.x), Mathf.Max(0.1f, cubeSize.y), Mathf.Max(0.02f, cubeSize.z));
            arrowHoleSize = new Vector2(Mathf.Clamp(arrowHoleSize.x, 0.02f, cubeSize.x * 0.8f), Mathf.Clamp(arrowHoleSize.y, 0.02f, cubeSize.y * 0.8f));
            Mesh previous = generatedMesh;
            generatedMesh = ArrowCutoutGeometry.CreateMesh(cubeSize, arrowHoleSize);
            generatedMesh.hideFlags = HideFlags.DontSave;
            GetComponent<MeshFilter>().sharedMesh = generatedMesh;

            MeshCollider meshCollider = GetComponent<MeshCollider>();
            if (addMeshCollider)
            {
                if (meshCollider == null) meshCollider = gameObject.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = generatedMesh;
                meshCollider.convex = false;
            }
            else if (meshCollider != null)
            {
                meshCollider.sharedMesh = null;
            }

            if (previous != null && previous != generatedMesh)
            {
                if (Application.isPlaying) Destroy(previous); else DestroyImmediate(previous);
            }
        }

        private void OnDestroy()
        {
            if (generatedMesh == null) return;
            if (Application.isPlaying) Destroy(generatedMesh); else DestroyImmediate(generatedMesh);
        }
    }
}
