using UnityEngine;
using UnityEngine.InputSystem;


[RequireComponent(typeof(Rigidbody2D), typeof(SoftBlob))]
public class BlobController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float acceleration = 40f;
    [SerializeField] private float jumpSpeed = 11f;
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private float groundCheckExtra = 0.3f;

    Rigidbody2D core;
    SoftBlob blob;
    float input;
    bool jumpQueued;

    void Awake()
    {
        core = GetComponent<Rigidbody2D>();
        blob = GetComponent<SoftBlob>();
    }

    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        input = 0f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input += 1f;

        if (kb.spaceKey.wasPressedThisFrame) jumpQueued = true;
    }

    bool Grounded()
    {

        return Physics2D.CircleCast(blob.Centroid, 0.3f, Vector2.down,
            blob.Radius + groundCheckExtra - 0.3f, groundMask);
    }

    void FixedUpdate()
    {
        Vector2 v = core.linearVelocity;
        v.x = Mathf.MoveTowards(v.x, input * moveSpeed, acceleration * Time.fixedDeltaTime);

        if (jumpQueued && Grounded()) v.y = jumpSpeed;
        jumpQueued = false;

        core.linearVelocity = v;
    }
}