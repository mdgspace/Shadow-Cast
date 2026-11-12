using System;
using UnityEngine;

namespace ShadowCast.Core.Campaign
{
    /// <summary>Finds and confirms a vertical room surface before a campaign level is built.</summary>
    public sealed class WallPlacementController : MonoBehaviour
    {
        [SerializeField] private Camera playerCamera;
        [SerializeField] private LayerMask roomWallMask = ~0;
        [SerializeField, Min(0.01f)] private float maxDistance = 5f;
        [SerializeField, Min(0f)] private float wallStandOff = 0.03f;
        [SerializeField] private Color previewColor = new Color(0.2f, 0.9f, 1f, 0.9f);

        public event Action<bool> CandidateChanged;
        public bool HasCandidate { get; private set; }
        public Pose CandidatePose { get; private set; }
        private LineRenderer previewFrame;
        private Material previewMaterial;
        private bool placementActive;

        private void Awake()
        {
            if (playerCamera == null) playerCamera = Camera.main;
            CreatePreviewFrame();
        }

        private void Update()
        {
            if (!placementActive || playerCamera == null) return;
            Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            bool found = Physics.Raycast(ray, out RaycastHit hit, maxDistance, roomWallMask, QueryTriggerInteraction.Ignore)
                && Mathf.Abs(Vector3.Dot(hit.normal, Vector3.up)) < 0.35f;
            SetCandidate(found, found ? new Pose(hit.point + hit.normal * wallStandOff, Quaternion.LookRotation(-hit.normal, Vector3.up)) : default);
        }

        /// <summary>Adapter point for Meta Scene API or a project-specific wall-placement provider.</summary>
        public void SetCandidate(Pose pose, bool isRoomWall)
        {
            SetCandidate(isRoomWall, pose);
        }

        public bool Confirm(CampaignFlowController flow)
        {
            if (!HasCandidate || flow == null) return false;
            flow.ConfirmWallPlacement(CandidatePose);
            return true;
        }

        public void Initialize(Camera camera)
        {
            playerCamera = camera != null ? camera : Camera.main;
        }

        public void SetPlacementActive(bool active)
        {
            placementActive = active;
            if (!active) SetCandidate(false, default);
        }

        public void SetPreviewSize(float width, float height)
        {
            if (previewFrame == null) return;
            float halfWidth = Mathf.Max(0.5f, width) * 0.5f;
            float halfHeight = Mathf.Max(0.5f, height) * 0.5f;
            previewFrame.SetPositions(new[]
            {
                new Vector3(-halfWidth, -halfHeight, 0f),
                new Vector3(halfWidth, -halfHeight, 0f),
                new Vector3(halfWidth, halfHeight, 0f),
                new Vector3(-halfWidth, halfHeight, 0f)
            });
        }

        private void CreatePreviewFrame()
        {
            GameObject previewObject = new GameObject("WallPlacementPreview");
            previewObject.transform.SetParent(transform, false);
            previewFrame = previewObject.AddComponent<LineRenderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                previewMaterial = new Material(shader);
                previewMaterial.color = previewColor;
                previewFrame.sharedMaterial = previewMaterial;
            }
            previewFrame.useWorldSpace = false;
            previewFrame.loop = true;
            previewFrame.positionCount = 4;
            previewFrame.startWidth = 0.012f;
            previewFrame.endWidth = 0.012f;
            previewFrame.startColor = previewColor;
            previewFrame.endColor = previewColor;
            previewFrame.enabled = false;
        }

        private void SetCandidate(bool available, Pose pose)
        {
            bool changed = HasCandidate != available || (available &&
                (Vector3.Distance(CandidatePose.position, pose.position) > 0.005f || Quaternion.Angle(CandidatePose.rotation, pose.rotation) > 1f));
            HasCandidate = available;
            CandidatePose = pose;
            if (previewFrame != null)
            {
                previewFrame.enabled = available;
                if (available) previewFrame.transform.SetPositionAndRotation(pose.position, pose.rotation);
            }
            if (changed) CandidateChanged?.Invoke(available);
        }

        private void OnDestroy()
        {
            if (previewMaterial != null) Destroy(previewMaterial);
        }
    }
}
