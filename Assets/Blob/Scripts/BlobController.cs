using ShadowCast.Core.Campaign;
using UnityEngine;

/// <summary>Compatibility adapter for existing Blob prefabs; campaign movement owns the physics.</summary>
[RequireComponent(typeof(Rigidbody2D), typeof(SoftBlob), typeof(WallPlatformerController2D))]
public sealed class BlobController : MonoBehaviour
{
    private WallPlatformerController2D movement;

    private void Awake() => movement = GetComponent<WallPlatformerController2D>();

    public void SetMovementInput(float value) => movement.SetMovementInput(value);
    public void Jump() => movement.RequestJump();
    public void ReleaseJump() => movement.ReleaseJump();
    public void Dash() => movement.RequestDash();
}
