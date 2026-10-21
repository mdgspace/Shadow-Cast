using UnityEngine;

/// <summary>
/// 2D Character Controller designed for the Shadow Parkour wall plane.
/// Demonstrates solid platform movement and sinking/falling through soft shadow quicksand.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class ShadowCharacter2D : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("Horizontal running speed along the wall.")]
    public float moveSpeed = 4.0f;

    [Tooltip("Standard jumping impulse when grounded on a solid crisp shadow.")]
    public float jumpForce = 6.5f;

    [Tooltip("LayerMask used to identify ground/colliders.")]
    public LayerMask groundLayer = ~0;

    [Header("Visual Feedback (Optional)")]
    [Tooltip("Optional sprite or mesh renderer to tint when sinking.")]
    public Renderer characterRenderer;

    public Color normalColor = Color.green;
    public Color sinkingColor = new Color(0.9f, 0.4f, 0.1f, 1f);

    [Header("State (Read-Only)")]
    public bool isGrounded = false;
    public bool isSubmergedInQuicksand = false;
    public float currentQuicksandSoftness = 0f;
    public float currentSinkSpeed = 0f;

    private Rigidbody2D rb;
    private Collider2D col;
    private ShadowQuicksandEffector activeQuicksand;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();

        // Ensure 2D physics constraints (lock rotation)
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        if (characterRenderer == null)
        {
            characterRenderer = GetComponentInChildren<Renderer>();
        }
    }

    private void Update()
    {
        CheckGrounded();
        HandleJump();
        UpdateVisuals();
    }

    private void FixedUpdate()
    {
        HandleHorizontalMovement();
    }

    private void HandleHorizontalMovement()
    {
        float h = Input.GetAxisRaw("Horizontal");

        // When in quicksand, horizontal movement is partially restricted by viscosity
        float currentSpeed = moveSpeed;
        if (isSubmergedInQuicksand && activeQuicksand != null)
        {
            currentSpeed *= Mathf.Lerp(0.85f, 0.25f, activeQuicksand.softness);
        }

        Vector2 vel = rb.linearVelocity;
        vel.x = h * currentSpeed;

        // Note: Vertical sinking is driven by ShadowQuicksandEffector during quicksand immersion
        rb.linearVelocity = vel;
    }

    private void HandleJump()
    {
        bool jumpPressed = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow);

        if (jumpPressed)
        {
            if (isGrounded)
            {
                // Solid jump
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            }
            else if (isSubmergedInQuicksand && activeQuicksand != null && activeQuicksand.allowStruggleJump)
            {
                // Struggle jump inside quicksand (strength depends on shadow softness)
                float struggleMultiplier = Mathf.Lerp(activeQuicksand.struggleJumpDamping, 0.15f, activeQuicksand.softness);
                rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce * struggleMultiplier);
            }
        }
    }

    private void CheckGrounded()
    {
        // Check if character's feet are touching a solid crisp platform
        Bounds b = col.bounds;
        Vector2 feetPos = new Vector2(b.center.x, b.min.y);
        Vector2 checkSize = new Vector2(b.size.x * 0.8f, 0.05f);

        Collider2D[] hits = Physics2D.OverlapBoxAll(feetPos, checkSize, 0f, groundLayer);
        isGrounded = false;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider2D hit = hits[i];
            if (hit == col || hit.isTrigger) continue;

            // Touching non-trigger surface
            isGrounded = true;
            break;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        ShadowQuicksandEffector effector = other.GetComponent<ShadowQuicksandEffector>();
        if (effector != null)
        {
            activeQuicksand = effector;
            isSubmergedInQuicksand = true;
            currentQuicksandSoftness = effector.softness;
            currentSinkSpeed = effector.GetCurrentSinkRate();
        }
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        ShadowQuicksandEffector effector = other.GetComponent<ShadowQuicksandEffector>();
        if (effector != null)
        {
            activeQuicksand = effector;
            isSubmergedInQuicksand = true;
            currentQuicksandSoftness = effector.softness;
            currentSinkSpeed = effector.GetCurrentSinkRate();
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        ShadowQuicksandEffector effector = other.GetComponent<ShadowQuicksandEffector>();
        if (effector != null && activeQuicksand == effector)
        {
            activeQuicksand = null;
            isSubmergedInQuicksand = false;
            currentQuicksandSoftness = 0f;
            currentSinkSpeed = 0f;
        }
    }

    private void UpdateVisuals()
    {
        if (characterRenderer != null && characterRenderer.material != null)
        {
            if (isSubmergedInQuicksand)
            {
                characterRenderer.material.color = Color.Lerp(normalColor, sinkingColor, currentQuicksandSoftness);
            }
            else
            {
                characterRenderer.material.color = normalColor;
            }
        }
    }
}
