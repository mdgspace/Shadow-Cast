using System;
using UnityEngine;

namespace ShadowCast.Core.Campaign
{
    /// <summary>Responsive campaign movement for both the soft Blob and conventional 2D character prefabs.</summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class WallPlatformerController2D : MonoBehaviour
    {
        [Header("Horizontal Movement")]
        [SerializeField, Min(0.1f)] private float maxMoveSpeed = 3.8f;
        [SerializeField, Min(0.1f)] private float groundAcceleration = 32f;
        [SerializeField, Min(0.1f)] private float groundDeceleration = 44f;
        [SerializeField, Min(0.1f)] private float airAcceleration = 20f;
        [SerializeField, Min(0.1f)] private float airDeceleration = 12f;

        [Header("Jump Feel")]
        [SerializeField, Min(0.1f)] private float jumpSpeed = 6.2f;
        [SerializeField, Min(0f)] private float coyoteTime = 0.13f;
        [SerializeField, Min(0f)] private float jumpBufferTime = 0.14f;
        [SerializeField, Min(0f)] private float postJumpGroundLockTime = 0.08f;
        [SerializeField, Range(0.1f, 1f)] private float jumpReleaseMultiplier = 0.48f;
        [SerializeField, Min(1f)] private float fallingGravityMultiplier = 1.65f;
        [SerializeField, Min(0.1f)] private float maxFallSpeed = 11f;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField, Min(0.01f)] private float groundCheckRadius = 0.08f;

        [Header("Dash Feel")]
        [SerializeField, Min(0.1f)] private float dashSpeed = 8.5f;
        [SerializeField, Min(0.01f)] private float dashDuration = 0.16f;
        [SerializeField, Min(0f)] private float dashRecoveryTime = 0.08f;

        [Header("Ability Inventory (Runtime)")]
        [SerializeField, Min(0)] private int doubleJumpUses;
        [SerializeField, Min(0)] private int dashUses;

        private Rigidbody2D body;
        private Collider2D bodyCollider;
        private SoftBlob softBlob;
        private float baseGravityScale;
        private float moveInput;
        private float facingDirection = 1f;
        private float coyoteTimer;
        private float jumpBufferTimer;
        private float groundLockTimer;
        private float dashTimer;
        private float dashRecoveryTimer;
        private float dashDirection;
        private bool jumpReleaseRequested;
        private bool dashRequested;
        private bool controlsEnabled = true;
        private CampaignFlowController flowController;
        private Transform wallTransform;

        public event Action<int, int> AbilityUsesChanged;
        public int DoubleJumpUses => doubleJumpUses;
        public int DashUses => dashUses;
        public bool IsDashing => dashTimer > 0f;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            softBlob = GetComponent<SoftBlob>();
            bodyCollider = GetComponent<Collider2D>();
            if (bodyCollider == null && softBlob == null) bodyCollider = gameObject.AddComponent<CapsuleCollider2D>();
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            baseGravityScale = Mathf.Max(0.01f, body.gravityScale);
        }

        private void FixedUpdate()
        {
            if (!controlsEnabled) return;

            groundLockTimer = Mathf.Max(0f, groundLockTimer - Time.fixedDeltaTime);
            bool grounded = groundLockTimer <= 0f && IsGrounded();
            coyoteTimer = grounded ? coyoteTime : Mathf.Max(0f, coyoteTimer - Time.fixedDeltaTime);
            jumpBufferTimer = Mathf.Max(0f, jumpBufferTimer - Time.fixedDeltaTime);
            dashRecoveryTimer = Mathf.Max(0f, dashRecoveryTimer - Time.fixedDeltaTime);

            if (dashTimer > 0f)
            {
                dashTimer -= Time.fixedDeltaTime;
                SetGravityScale(0f);
                ApplyVelocity(new Vector2(dashDirection * dashSpeed, 0f));
                jumpReleaseRequested = false;
                CheckFallFailure();
                return;
            }

            SetGravityScale(body.linearVelocity.y < -0.05f ? baseGravityScale * fallingGravityMultiplier : baseGravityScale);

            if (dashRequested && dashRecoveryTimer <= 0f && dashUses > 0) BeginDash();
            dashRequested = false;
            if (dashTimer > 0f) return;

            Vector2 velocity = body.linearVelocity;
            float targetSpeed = moveInput * maxMoveSpeed;
            bool accelerating = Mathf.Abs(targetSpeed) > 0.01f && Mathf.Sign(targetSpeed) == Mathf.Sign(velocity.x);
            float rate = grounded
                ? (accelerating ? groundAcceleration : groundDeceleration)
                : (accelerating ? airAcceleration : airDeceleration);
            velocity.x = Mathf.MoveTowards(velocity.x, targetSpeed, rate * Time.fixedDeltaTime);

            if (jumpBufferTimer > 0f)
            {
                if (coyoteTimer > 0f)
                {
                    velocity.y = jumpSpeed;
                    coyoteTimer = 0f;
                    jumpBufferTimer = 0f;
                    groundLockTimer = postJumpGroundLockTime;
                }
                else if (doubleJumpUses > 0)
                {
                    velocity.y = jumpSpeed;
                    doubleJumpUses--;
                    jumpBufferTimer = 0f;
                    groundLockTimer = postJumpGroundLockTime;
                    AbilityUsesChanged?.Invoke(doubleJumpUses, dashUses);
                }
            }

            if (jumpReleaseRequested && velocity.y > 0f) velocity.y *= jumpReleaseMultiplier;
            jumpReleaseRequested = false;
            velocity.y = Mathf.Max(velocity.y, -maxFallSpeed);
            ApplyVelocity(velocity);
            CheckFallFailure();
        }

        private void BeginDash()
        {
            dashUses--;
            dashDirection = Mathf.Abs(moveInput) > 0.05f ? Mathf.Sign(moveInput) : facingDirection;
            dashTimer = dashDuration;
            dashRecoveryTimer = dashDuration + dashRecoveryTime;
            SetGravityScale(0f);
            ApplyVelocity(new Vector2(dashDirection * dashSpeed, 0f));
            AbilityUsesChanged?.Invoke(doubleJumpUses, dashUses);
        }

        public void SetMovementInput(float value)
        {
            moveInput = controlsEnabled ? Mathf.Clamp(value, -1f, 1f) : 0f;
            if (Mathf.Abs(moveInput) > 0.05f) facingDirection = Mathf.Sign(moveInput);
        }

        public void RequestJump() { if (controlsEnabled) jumpBufferTimer = jumpBufferTime; }
        public void ReleaseJump() { if (controlsEnabled) jumpReleaseRequested = true; }
        public void RequestDash() { if (controlsEnabled) dashRequested = true; }

        public void GrantDoubleJumpUses(int uses)
        {
            doubleJumpUses += Mathf.Max(0, uses);
            AbilityUsesChanged?.Invoke(doubleJumpUses, dashUses);
        }

        public void GrantDashUses(int uses)
        {
            dashUses += Mathf.Max(0, uses);
            AbilityUsesChanged?.Invoke(doubleJumpUses, dashUses);
        }

        public void SetControlsEnabled(bool enabled)
        {
            controlsEnabled = enabled;
            if (!enabled)
            {
                moveInput = 0f;
                dashRequested = false;
                jumpBufferTimer = 0f;
            }
        }

        public void ResetAt(Vector3 position)
        {
            if (body == null) body = GetComponent<Rigidbody2D>();
            if (softBlob == null) softBlob = GetComponent<SoftBlob>();
            if (softBlob != null) softBlob.ResetAt(position);
            else body.position = position;
            body.linearVelocity = Vector2.zero;
            SetGravityScale(baseGravityScale);
            moveInput = 0f;
            coyoteTimer = 0f;
            jumpBufferTimer = 0f;
            groundLockTimer = 0f;
            dashTimer = 0f;
            dashRecoveryTimer = 0f;
            controlsEnabled = true;
        }

        public void ConfigureFailureBounds(CampaignFlowController flow, Transform wall)
        {
            flowController = flow;
            wallTransform = wall;
        }

        private bool IsGrounded()
        {
            Vector2 center;
            float radius;
            float distance;
            if (softBlob != null)
            {
                center = softBlob.Centroid;
                radius = Mathf.Max(0.02f, softBlob.Radius * 0.35f);
                distance = Mathf.Max(groundCheckRadius, softBlob.Radius * 0.72f);
            }
            else
            {
                Bounds bounds = bodyCollider.bounds;
                center = bounds.center;
                radius = groundCheckRadius;
                distance = bounds.extents.y + groundCheckRadius;
            }

            RaycastHit2D[] hits = Physics2D.CircleCastAll(center, radius, Vector2.down, distance, groundMask);
            for (int i = 0; i < hits.Length; i++)
            {
                Collider2D hit = hits[i].collider;
                if (hit == null || hit.isTrigger || hit.transform == transform || hit.transform.IsChildOf(transform)) continue;
                if (hits[i].normal.y > 0.25f) return true;
            }
            return false;
        }

        private void SetGravityScale(float value)
        {
            body.gravityScale = value;
            if (softBlob != null) softBlob.SetGravityScale(value);
        }

        private void ApplyVelocity(Vector2 velocity)
        {
            if (softBlob != null) softBlob.SetVelocity(velocity);
            else body.linearVelocity = velocity;
        }

        private void CheckFallFailure()
        {
            if (flowController == null || wallTransform == null) return;
            Vector3 localPosition = wallTransform.InverseTransformPoint(transform.position);
            // InverseTransformPoint already removes the wall dimensions.
            bool outsideSides = Mathf.Abs(localPosition.x) > 0.5f + 0.75f / Mathf.Abs(wallTransform.lossyScale.x);
            bool belowWall = localPosition.y < -0.5f - 0.75f / Mathf.Abs(wallTransform.lossyScale.y);
            if (!outsideSides && !belowWall) return;
            SetControlsEnabled(false);
            flowController.FailLevel("You fell off the shadow path. Retry to rebuild this level.");
        }
    }
}
