using UnityEngine;
using UnityEngine.XR;

namespace ShadowCast.Core.Campaign
{
    /// <summary>Keeps a visible background until the actual passthrough compositor is ready.</summary>
    public sealed class ShadowCastPassthroughState : MonoBehaviour
    {
        private Camera view;
        private OVRPassthroughLayer layer;
        private float nextCheck;
        private bool requested;
        private float started;
        private bool warned;
        public string RuntimeStatus { get; private set; }

        public void Initialize(Camera camera)
        {
            view = camera;
            started = Time.unscaledTime;
            UpdateBackground(false);
        }

        private void Update()
        {
            if (view == null || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + .25f;
            bool active = XRSettings.isDeviceActive;
            if (active && !requested && OVRManager.IsInsightPassthroughSupported())
            {
                OVRManager manager = FindAnyObjectByType<OVRManager>();
                if (manager == null) manager = gameObject.AddComponent<OVRManager>();
                manager.isInsightPassthroughEnabled = true;
                layer = FindAnyObjectByType<OVRPassthroughLayer>();
                if (layer == null) layer = gameObject.AddComponent<OVRPassthroughLayer>();
                layer.hidden = false;
                layer.textureOpacity = 1f;
                layer.edgeRenderingEnabled = false;
                requested = true;
            }
            bool ready = active && requested && layer != null && layer.isActiveAndEnabled && !layer.hidden &&
                OVRManager.IsInsightPassthroughInitialized();
            RuntimeStatus = ready ? "Ready" : !active ? "No active XR display" : requested ? "Waiting for passthrough compositor" : "Passthrough unavailable from current runtime";
            if (active && !ready && !warned && Time.unscaledTime - started > 8f)
            {
                warned = true;
                Debug.LogWarning("[Shadow Cast] " + RuntimeStatus + ". For room video over Link, enable Developer Runtime Features and Passthrough over Meta Quest Link in the Link PC app under Settings > Beta, then restart Unity.", this);
            }
            UpdateBackground(ready);
        }

        private void UpdateBackground(bool transparent)
        {
            if (view == null) return;
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = transparent ? Color.clear : new Color(.035f, .055f, .09f, 1f);
        }
    }
}
