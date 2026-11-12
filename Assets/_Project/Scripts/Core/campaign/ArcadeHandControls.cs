using UnityEngine;

namespace ShadowCast.Core.Campaign
{
    /// <summary>Routes an imported Figma arcade panel's joystick and jump controls to the character.</summary>
    public sealed class ArcadeHandControls : MonoBehaviour
    {
        [SerializeField] private Transform nonDominantHandAnchor;
        [SerializeField] private Transform panelRoot;
        [SerializeField] private WallPlatformerController2D character;
        [SerializeField] private Vector3 handLocalPosition = new Vector3(0.08f, -0.05f, 0.18f);
        [SerializeField] private Vector3 handLocalEulerAngles = new Vector3(8f, 0f, -12f);

        public void SetCharacter(WallPlatformerController2D target) => character = target;

        private void LateUpdate()
        {
            if (nonDominantHandAnchor == null || panelRoot == null) return;
            panelRoot.SetPositionAndRotation(
                nonDominantHandAnchor.TransformPoint(handLocalPosition),
                nonDominantHandAnchor.rotation * Quaternion.Euler(handLocalEulerAngles));
        }

        public void SetMovementInput(float value) => character?.SetMovementInput(value);
        public void Jump() => character?.RequestJump();
        public void ReleaseJump() => character?.ReleaseJump();
        public void Dash() => character?.RequestDash();

        public void RecenterToCurrentHandPose()
        {
            if (nonDominantHandAnchor == null || panelRoot == null) return;
            handLocalPosition = nonDominantHandAnchor.InverseTransformPoint(panelRoot.position);
            handLocalEulerAngles = (Quaternion.Inverse(nonDominantHandAnchor.rotation) * panelRoot.rotation).eulerAngles;
        }

        public void ReleaseControls() => character?.SetMovementInput(0f);
    }
}
