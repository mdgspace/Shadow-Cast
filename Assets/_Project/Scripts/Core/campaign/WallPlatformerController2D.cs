using UnityEngine;

namespace ShadowCast.Core.Campaign
{
    /// <summary>Minimal wall-plane platformer controller driven by the virtual arcade panel.</summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class WallPlatformerController2D : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float moveSpeed = 2.2f;
        [SerializeField, Min(0.1f)] private float jumpSpeed = 4.2f;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField, Min(0.01f)] private float groundCheckRadius = 0.06f;

        private Rigidbody2D body;
        private Collider2D bodyCollider;
        private float moveInput;
        private bool jumpRequested;
        private bool controlsEnabled = true;
        private CampaignFlowController flowController;
        private Transform wallTransform;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            bodyCollider = GetComponent<Collider2D>();
            if (bodyCollider == null) bodyCollider = gameObject.AddComponent<CapsuleCollider2D>();
            body.freezeRotation = true;
        }

        private void FixedUpdate()
        {
            if (!controlsEnabled) return;
            Vector2 velocity = body.linearVelocity;
            velocity.x = moveInput * moveSpeed;
            if (jumpRequested && IsGrounded()) velocity.y = jumpSpeed;
            body.linearVelocity = velocity;
            jumpRequested = false;
            CheckFallFailure();
        }

        public void SetMovementInput(float value) => moveInput = controlsEnabled ? Mathf.Clamp(value, -1f, 1f) : 0f;
        public void RequestJump() { if (controlsEnabled) jumpRequested = true; }
        public void SetControlsEnabled(bool enabled) { controlsEnabled = enabled; if (!enabled) SetMovementInput(0f); }

        public void ResetAt(Vector3 position)
        {
            if (body == null) body = GetComponent<Rigidbody2D>();
            body.position = position;
            body.linearVelocity = Vector2.zero;
            moveInput = 0f;
            jumpRequested = false;
            controlsEnabled = true;
        }

        public void ConfigureFailureBounds(CampaignFlowController flow, Transform wall)
        {
            flowController = flow;
            wallTransform = wall;
        }

        private bool IsGrounded()
        {
            Bounds bounds = bodyCollider.bounds;
            Vector2 check = new Vector2(bounds.center.x, bounds.min.y - groundCheckRadius * 0.5f);
            Collider2D[] hits = Physics2D.OverlapCircleAll(check, groundCheckRadius, groundMask);
            for (int i = 0; i < hits.Length; i++)
                if (hits[i] != null && hits[i] != bodyCollider && !hits[i].isTrigger)
                    return true;
            return false;
        }

        private void CheckFallFailure()
        {
            if (flowController == null || wallTransform == null) return;
            Vector3 localPosition = wallTransform.InverseTransformPoint(transform.position);
            bool outsideSides = Mathf.Abs(localPosition.x) > wallTransform.localScale.x * 0.5f + 0.75f;
            bool belowWall = localPosition.y < -wallTransform.localScale.y * 0.5f - 0.75f;
            if (outsideSides || belowWall)
            {
                SetControlsEnabled(false);
                flowController.FailLevel("You fell off the shadow path. Retry to rebuild this level.");
            }
        }
    }
}
