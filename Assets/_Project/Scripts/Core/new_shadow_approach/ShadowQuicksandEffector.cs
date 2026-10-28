using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attached to diffuse/soft shadow colliders.
/// Provides a "quicksand / buoyancy" physical effect:
/// 2D characters entering this volume will sink and fall through at a rate
/// directly proportional to the shadow's softness (higher softness = faster sink rate).
/// </summary>
[SelectionBase]
[DisallowMultipleComponent]
[RequireComponent(typeof(PolygonCollider2D))]
public class ShadowQuicksandEffector : MonoBehaviour
{
    [Header("Shadow Diffusion & Softness")]
    [Tooltip("Softness factor (0.0 = barely soft / high buoyancy / very slow sink, 1.0 = extremely diffuse / zero buoyancy / fast sink).")]
    [Range(0f, 1f)]
    public float softness = 0.5f;

    [Tooltip("Unique platform ID generated during bake.")]
    public int platformId;

    [Header("Quicksand Sinking Dynamics")]
    [Tooltip("Downward sinking velocity when softness is at minimum (0.0). Units: m/s.")]
    public float minSinkSpeed = 0.35f;

    [Tooltip("Downward sinking velocity when softness is at maximum (1.0). Units: m/s.")]
    public float maxSinkSpeed = 3.5f;

    [Tooltip("Acceleration toward the target sink velocity (how quickly quicksand pulls the character in).")]
    public float sinkAcceleration = 8.0f;

    [Header("Buoyancy & Resistance")]
    [Tooltip("Upward buoyancy counteracting gravity. Scaled by (1 - softness).")]
    [Range(0f, 1f)]
    public float maxBuoyancyForce = 1.0f;

    [Tooltip("Viscous drag applied to horizontal character movement inside the quicksand.")]
    [Range(0f, 20f)]
    public float horizontalViscosity = 5.0f;

    [Tooltip("Whether struggling (jumping) inside the quicksand is permitted.")]
    public bool allowStruggleJump = true;

    [Tooltip("Multiplier for jump impulse while submerged in quicksand (damped by softness).")]
    [Range(0.1f, 1f)]
    public float struggleJumpDamping = 0.5f;

    [Header("Native Effector Integration (Optional)")]
    [Tooltip("If true, also configures Unity's built-in AreaEffector2D on this collider for a force-based quicksand pull.")]
    public bool useNativeBuoyancyEffector = false;

    // Track active characters inside this quicksand trigger
    private class SubmergedCharacter
    {
        public Rigidbody2D rb;
        public Collider2D col;
        public float originalGravityScale;
        public float originalLinearDrag;
    }

    private readonly Dictionary<Collider2D, SubmergedCharacter> submergedMap = new Dictionary<Collider2D, SubmergedCharacter>();
    private readonly List<Collider2D> toRemove = new List<Collider2D>();

    private PolygonCollider2D polyCollider;
    private AreaEffector2D areaEffector;

    private void Awake()
    {
        polyCollider = GetComponent<PolygonCollider2D>();
        if (polyCollider != null)
        {
            polyCollider.isTrigger = true;
        }

        // Note: native effector setup is deferred to SetSoftness() call
        // because Awake() fires before the baker sets the softness value
    }

    /// <summary>
    /// Updates the softness of this soft shadow and adjusts physics parameters accordingly.
    /// </summary>
    public void SetSoftness(float newSoftness)
    {
        softness = Mathf.Clamp01(newSoftness);

        // Set up native effector on first call if enabled
        if (useNativeBuoyancyEffector && areaEffector == null)
        {
            if (polyCollider == null) polyCollider = GetComponent<PolygonCollider2D>();
            if (polyCollider != null) SetupNativeBuoyancy();
        }

        if (areaEffector != null)
        {
            // Higher softness = stronger downward force = faster sinking
            areaEffector.forceMagnitude = Mathf.Lerp(minSinkSpeed, maxSinkSpeed, softness) * 2.0f;
            areaEffector.linearDamping = Mathf.Lerp(8f, 2f, softness);
        }
    }

    private void SetupNativeBuoyancy()
    {
        areaEffector = GetComponent<AreaEffector2D>();
        if (areaEffector == null)
        {
            areaEffector = gameObject.AddComponent<AreaEffector2D>();
        }
        polyCollider.usedByEffector = true;
        areaEffector.useColliderMask = false;
        areaEffector.forceAngle = -90f; // Downward sinking force
        areaEffector.forceMagnitude = Mathf.Lerp(minSinkSpeed, maxSinkSpeed, softness) * 2.0f;
        areaEffector.forceVariation = 0f;
        areaEffector.linearDamping = Mathf.Lerp(8f, 2f, softness);
        areaEffector.angularDamping = 0.5f;
        areaEffector.useGlobalAngle = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Rigidbody2D rb = other.attachedRigidbody;
        if (rb == null || rb.bodyType != RigidbodyType2D.Dynamic)
            return;

        if (!submergedMap.ContainsKey(other))
        {
            SubmergedCharacter sub = new SubmergedCharacter
            {
                rb = rb,
                col = other,
                originalGravityScale = rb.gravityScale,
                originalLinearDrag = rb.linearDamping
            };
            submergedMap.Add(other, sub);
        }
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (!submergedMap.TryGetValue(other, out SubmergedCharacter sub) || sub.rb == null)
            return;

        // If the character is also currently standing on a crisp solid platform, don't sink
        if (IsCharacterOnSolidPlatform(other))
        {
            return;
        }

        ApplyQuicksandPhysics(sub);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (submergedMap.TryGetValue(other, out SubmergedCharacter sub))
        {
            if (sub.rb != null)
            {
                // Restore standard rigidbody parameters
                sub.rb.gravityScale = sub.originalGravityScale;
                sub.rb.linearDamping = sub.originalLinearDrag;
            }
            submergedMap.Remove(other);
        }
    }

    private void FixedUpdate()
    {
        // Clean up any destroyed or disabled colliders
        if (submergedMap.Count > 0)
        {
            toRemove.Clear();
            foreach (var kvp in submergedMap)
            {
                if (kvp.Key == null || !kvp.Key.gameObject.activeInHierarchy || kvp.Value.rb == null)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                submergedMap.Remove(toRemove[i]);
            }
        }
    }

    /// <summary>
    /// Applies buoyancy damping and quicksand sinking forces to the submerged character.
    /// </summary>
    private void ApplyQuicksandPhysics(SubmergedCharacter sub)
    {
        Rigidbody2D rb = sub.rb;

        // Current sinking speed based directly on shadow softness:
        // Softness 0.0 (crisp edge) -> very slow sink (minSinkSpeed)
        // Softness 1.0 (very diffuse) -> fast sink (maxSinkSpeed)
        float targetSinkVelocity = -Mathf.Lerp(minSinkSpeed, maxSinkSpeed, softness);

        // Counteract standard gravity using buoyancy (denser fluid when shadow is less diffuse)
        float buoyancyRatio = Mathf.Clamp01((1.0f - softness) * maxBuoyancyForce);
        rb.gravityScale = sub.originalGravityScale * (1.0f - buoyancyRatio * 0.85f);

        // Smoothly guide Y velocity toward target sink speed
        Vector2 vel = rb.linearVelocity;
        vel.y = Mathf.MoveTowards(vel.y, targetSinkVelocity, sinkAcceleration * Time.fixedDeltaTime);

        // Apply viscous drag horizontally so wading through quicksand feels sluggish
        vel.x = Mathf.MoveTowards(vel.x, 0f, horizontalViscosity * Time.fixedDeltaTime);

        rb.linearVelocity = vel;
    }

    /// <summary>
    /// Checks whether the character is currently contacting a solid crisp shadow platform.
    /// </summary>
    private bool IsCharacterOnSolidPlatform(Collider2D characterCol)
    {
        ContactPoint2D[] contacts = new ContactPoint2D[8];
        int count = characterCol.GetContacts(contacts);
        for (int i = 0; i < count; i++)
        {
            ContactPoint2D contact = contacts[i];
            if (contact.collider != null && !contact.collider.isTrigger)
            {
                if (contact.collider.GetComponent<ShadowSolidPlatform>() != null)
                {
                    // Touching solid platform from above
                    if (contact.normal.y > 0.5f)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Allows character controllers to query current sinking state and softness.
    /// </summary>
    public float GetCurrentSinkRate()
    {
        return Mathf.Lerp(minSinkSpeed, maxSinkSpeed, softness);
    }

    private void OnDrawGizmosSelected()
    {
        if (polyCollider != null)
        {
            Gizmos.color = Color.Lerp(Color.yellow, new Color(1f, 0.3f, 0f, 0.8f), softness);
            Gizmos.matrix = transform.localToWorldMatrix;
            for (int i = 0; i < polyCollider.pathCount; i++)
            {
                Vector2[] path = polyCollider.GetPath(i);
                for (int j = 0; j < path.Length; j++)
                {
                    Vector2 next = path[(j + 1) % path.Length];
                    Gizmos.DrawLine(path[j], next);
                }
            }
        }
    }
}
