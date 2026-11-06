namespace Leed3D.Kernel.Physics;

/// <summary>
/// Configuration parameters for the first-person <see cref="WalkController"/> player capsule.
/// </summary>
public sealed class WalkPlayerConfig
{
    public float Height { get; set; } = 1.8f;
    public float Radius { get; set; } = 0.4f;
    public float EyeHeight { get; set; } = 1.65f;

    public float MoveSpeed { get; set; } = 6.0f;
    public float SprintMultiplier { get; set; } = 1.6f;

    public float JumpVelocity { get; set; } = 6.5f;
    public float Gravity { get; set; } = 18.0f;

    public float MaxStepHeight { get; set; } = 0.35f;
    public float MaxSlopeDegrees { get; set; } = 50.0f;
}
