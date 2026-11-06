using System.Numerics;
using Leed3D.Kernel.Entities;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Scene;
using Leed3D.Runtime.Metadata;

namespace Leed3D.Kernel.Physics;

/// <summary>
/// 100% headless first-person capsule physics controller operating in Left-Handed Editor Space (+X Right, +Y Up, +Z Away).
/// Supports gravity, jumping, collide-and-slide wall resolution, stair step-up, slope limits,
/// <c>collision=none</c> pass-through, surface metadata reporting, and trigger/portal volumes.
/// </summary>
public sealed class WalkController
{
    private const float WallBottomClearance = 0.02f;
    private const float WallTopClearance = 0.02f;
    private const int MaxSlideIterations = 3;

    private Vector3 _position;
    private readonly List<CollisionTriggerVolume> _activeTriggers = [];
    private readonly List<string> _activeTriggerIds = [];
    private readonly HashSet<string> _cooldownPortalIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _consumedOneShotTriggers = new(StringComparer.OrdinalIgnoreCase);

    public WalkPlayerConfig Config { get; }
    public CollisionMeshWorld World { get; set; }

    /// <summary>
    /// Player feet position in world space (+X Right, +Y Up, +Z Away).
    /// </summary>
    public Vector3 Position
    {
        get => _position;
        set
        {
            _position = value;
            if (Velocity.Y <= 0.0f)
            {
                RefreshGroundState();
            }
            RefreshTriggers(allowTeleport: false);
        }
    }

    /// <summary>
    /// Alias for <see cref="Position"/> (bottom of player capsule).
    /// </summary>
    public Vector3 FeetPosition
    {
        get => _position;
        set => Position = value;
    }

    /// <summary>
    /// World-space first-person camera eye position (<c>Position + (0, EyeHeight, 0)</c>).
    /// </summary>
    public Vector3 EyePosition => new(_position.X, _position.Y + Config.EyeHeight, _position.Z);

    public Vector3 Velocity { get; set; } = Vector3.Zero;
    public float YawDegrees { get; set; }
    public float PitchDegrees { get; set; }

    public bool IsGrounded { get; private set; }
    public ElementMetadata? GroundSurfaceMetadata { get; private set; }
    public ElementMetadata? LastGroundSurfaceMetadata { get; private set; }
    public ElementMetadata? LastWallContactMetadata { get; private set; }
    public string? GroundMaterialId { get; private set; }

    /// <summary>
    /// Convenience accessor for the <c>surface</c> property on <see cref="GroundSurfaceMetadata"/>.
    /// </summary>
    public string? GroundSurfaceName => GroundSurfaceMetadata?.GetString("surface");

    public IReadOnlyList<CollisionTriggerVolume> ActiveTriggers => _activeTriggers;
    public IReadOnlyList<string> ActiveTriggerIds => _activeTriggerIds;
    public string? LastTeleportedPortalId { get; private set; }

    public WalkController(CollisionMeshWorld world, WalkPlayerConfig? config = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        World = world;
        Config = config ?? new WalkPlayerConfig();
        RefreshGroundState();
        RefreshTriggers(allowTeleport: false);
    }

    public WalkController(EditorScene scene, WalkPlayerConfig? config = null, EntitySchemaRegistry? entityRegistry = null)
        : this(CollisionMeshWorld.Build(scene, entityRegistry), config)
    {
    }

    /// <summary>
    /// Spawns or resets the player at the specified world position and orientation.
    /// </summary>
    public void Spawn(Vector3 position, float yawDegrees = 0.0f, float pitchDegrees = 0.0f)
    {
        _position = position;
        Velocity = Vector3.Zero;
        YawDegrees = yawDegrees;
        PitchDegrees = System.Math.Clamp(pitchDegrees, -89.0f, 89.0f);
        LastTeleportedPortalId = null;
        LastWallContactMetadata = null;
        _cooldownPortalIds.Clear();
        _consumedOneShotTriggers.Clear();

        // Depenetrate upward if spawn point is slightly embedded in a floor
        if (TryFindHighestFloor(_position.X, _position.Z, _position.Y - 0.05f, _position.Y + (Config.Height * 0.5f), out float floorY, out CollisionTriangle? floorTri))
        {
            _position.Y = floorY;
            IsGrounded = true;
            GroundSurfaceMetadata = floorTri!.Metadata;
            LastGroundSurfaceMetadata = floorTri.Metadata;
            GroundMaterialId = floorTri.MaterialId;
        }
        else
        {
            RefreshGroundState();
        }

        RefreshTriggers(allowTeleport: false);
    }

    /// <summary>
    /// Computes the world-space AABB enclosing the player capsule.
    /// </summary>
    public BoundingBox3D GetPlayerBounds()
    {
        return new BoundingBox3D(
            new Vector3(_position.X - Config.Radius, _position.Y, _position.Z - Config.Radius),
            new Vector3(_position.X + Config.Radius, _position.Y + Config.Height, _position.Z + Config.Radius));
    }

    /// <summary>
    /// Advances the physics simulation by <paramref name="dt"/> seconds.
    /// </summary>
    /// <param name="dt">Time step in seconds.</param>
    /// <param name="wishMoveLocal">Local movement input where X = Right/Left (+1/-1), Y = Forward/Back (+1/-1).</param>
    /// <param name="yawDegrees">Look yaw angle in degrees (0 = +Z, 90 = +X).</param>
    /// <param name="pitchDegrees">Look pitch angle in degrees.</param>
    /// <param name="jumpPressed">Whether jump was pressed this step.</param>
    /// <param name="sprintHeld">Whether the sprint speed multiplier is active.</param>
    public void Step(
        float dt,
        Vector2 wishMoveLocal,
        float yawDegrees,
        float pitchDegrees,
        bool jumpPressed,
        bool sprintHeld = false)
    {
        YawDegrees = yawDegrees;
        PitchDegrees = System.Math.Clamp(pitchDegrees, -89.0f, 89.0f);

        if (dt <= 0.0f)
        {
            if (Velocity.Y <= 0.0f)
            {
                RefreshGroundState();
            }
            RefreshTriggers(allowTeleport: true);
            return;
        }

        float clampedDt = System.Math.Min(dt, 0.25f);

        // Ensure ground state is current before evaluating gravity and jump
        if (Velocity.Y <= 0.0f)
        {
            RefreshGroundState();
        }

        // 1. Apply gravity to vertical velocity when airborne
        if (!IsGrounded)
        {
            Velocity = new Vector3(Velocity.X, Velocity.Y - (Config.Gravity * clampedDt), Velocity.Z);
        }

        // 2. Jump impulse when grounded
        if (IsGrounded && jumpPressed)
        {
            Velocity = new Vector3(Velocity.X, Config.JumpVelocity, Velocity.Z);
            IsGrounded = false;
            GroundSurfaceMetadata = null;
            GroundMaterialId = null;
        }

        // 3. Horizontal Collide-and-Slide + Stair Step-Up
        float yawRad = YawDegrees * (MathF.PI / 180.0f);
        Vector3 forwardXZ = new(MathF.Sin(yawRad), 0.0f, MathF.Cos(yawRad));
        Vector3 rightXZ = new(MathF.Cos(yawRad), 0.0f, -MathF.Sin(yawRad));

        Vector3 wishDir = (rightXZ * wishMoveLocal.X) + (forwardXZ * wishMoveLocal.Y);
        if (wishDir.LengthSquared() > 1.0f)
        {
            wishDir = Vector3.Normalize(wishDir);
        }

        float moveSpeed = Config.MoveSpeed * (sprintHeld ? Config.SprintMultiplier : 1.0f);
        Vector3 desiredHorizVel = wishDir * moveSpeed;
        Vector3 desiredHorizDelta = desiredHorizVel * clampedDt;

        bool steppedUpThisFrame = false;
        if (desiredHorizDelta.LengthSquared() > 1e-10f)
        {
            steppedUpThisFrame = MoveHorizontalWithStepUp(desiredHorizDelta, clampedDt, out Vector3 actualHorizVel);
            Velocity = new Vector3(actualHorizVel.X, Velocity.Y, actualHorizVel.Z);
        }
        else
        {
            Velocity = new Vector3(0.0f, Velocity.Y, 0.0f);
        }

        // 4. Vertical motion + Ground Snap & Slope Check
        if (steppedUpThisFrame)
        {
            Velocity = new Vector3(Velocity.X, 0.0f, Velocity.Z);
            IsGrounded = true;
        }
        else
        {
            float oldY = _position.Y;
            float targetY = oldY + (Velocity.Y * clampedDt);

            if (Velocity.Y > 0.0f)
            {
                // Moving upward (jumping): check ceiling collisions
                if (TryFindLowestCeiling(_position.X, _position.Z, oldY + Config.Height, targetY + Config.Height, out float ceilingY))
                {
                    targetY = ceilingY - Config.Height;
                    Velocity = new Vector3(Velocity.X, 0.0f, Velocity.Z);
                }

                _position = new Vector3(_position.X, targetY, _position.Z);
                IsGrounded = false;
                GroundSurfaceMetadata = null;
                GroundMaterialId = null;
            }
            else
            {
                // Moving downward or staying grounded: check walkable floor support
                float searchTopY = IsGrounded ? oldY + Config.MaxStepHeight + 0.02f : oldY + 0.05f;
                float snapDown = IsGrounded ? Config.MaxStepHeight + 0.05f : 0.03f;
                float searchBottomY = targetY - snapDown;

                if (TryFindHighestFloor(_position.X, _position.Z, searchBottomY, searchTopY, out float floorY, out CollisionTriangle? floorTri))
                {
                    _position = new Vector3(_position.X, floorY, _position.Z);
                    Velocity = new Vector3(Velocity.X, 0.0f, Velocity.Z);
                    IsGrounded = true;
                    GroundSurfaceMetadata = floorTri!.Metadata;
                    LastGroundSurfaceMetadata = floorTri.Metadata;
                    GroundMaterialId = floorTri.MaterialId;
                }
                else
                {
                    _position = new Vector3(_position.X, targetY, _position.Z);
                    IsGrounded = false;
                    GroundSurfaceMetadata = null;
                    GroundMaterialId = null;
                }
            }
        }

        // 5. Evaluate Trigger & Portal Volumes
        RefreshTriggers(allowTeleport: true);
    }

    private bool MoveHorizontalWithStepUp(Vector3 desiredDeltaXZ, float dt, out Vector3 actualHorizVel)
    {
        SimulateHorizontalSlide(
            _position,
            desiredDeltaXZ,
            dt,
            out Vector3 unsteppedPos,
            out Vector3 unsteppedVel,
            out bool hitWall,
            out CollisionTriangle? hitWallTri);

        if (hitWallTri is not null)
        {
            LastWallContactMetadata = hitWallTri.Metadata;
        }

        // Attempt stair step-up if blocked by a wall while grounded (or near ground)
        if (hitWall && (IsGrounded || Velocity.Y <= 0.0f) && Config.MaxStepHeight > 0.0f)
        {
            float stepRaise = Config.MaxStepHeight;

            // Ensure there is ceiling clearance to raise the capsule by MaxStepHeight
            if (!TryFindLowestCeiling(_position.X, _position.Z, _position.Y + Config.Height, _position.Y + Config.Height + stepRaise, out _))
            {
                Vector3 raisedStart = new(_position.X, _position.Y + stepRaise, _position.Z);
                SimulateHorizontalSlide(
                    raisedStart,
                    desiredDeltaXZ,
                    dt,
                    out Vector3 steppedHighPos,
                    out Vector3 steppedVel,
                    out _,
                    out _);

                Vector2 wishDir2D = new(desiredDeltaXZ.X, desiredDeltaXZ.Z);
                float wishLen = wishDir2D.Length();
                if (wishLen > 1e-6f)
                {
                    wishDir2D /= wishLen;
                }

                float unsteppedProgress = Vector2.Dot(new Vector2(unsteppedPos.X - _position.X, unsteppedPos.Z - _position.Z), wishDir2D);
                float steppedProgress = Vector2.Dot(new Vector2(steppedHighPos.X - _position.X, steppedHighPos.Z - _position.Z), wishDir2D);

                if (steppedProgress > unsteppedProgress + 1e-4f)
                {
                    // Find supporting ledge/step floor beneath the stepped position
                    bool foundFloor = TryFindHighestFloor(
                        steppedHighPos.X,
                        steppedHighPos.Z,
                        _position.Y - 0.02f,
                        steppedHighPos.Y + 0.02f,
                        out float stepFloorY,
                        out CollisionTriangle? stepFloorTri);

                    // Also check low wall/curb top height within cylinder footprint
                    bool foundLowCurb = TryFindLowObstacleTop(
                        steppedHighPos.X,
                        steppedHighPos.Z,
                        _position.Y,
                        _position.Y + Config.MaxStepHeight,
                        out float curbTopY,
                        out CollisionTriangle? curbTri);

                    float targetStepY = _position.Y;
                    CollisionTriangle? supportTri = null;

                    if (foundFloor && stepFloorY > targetStepY)
                    {
                        targetStepY = stepFloorY;
                        supportTri = stepFloorTri;
                    }

                    if (foundLowCurb && curbTopY > targetStepY)
                    {
                        targetStepY = curbTopY;
                        supportTri ??= curbTri;
                    }

                    float stepDeltaY = targetStepY - _position.Y;
                    if (stepDeltaY > 1e-4f && stepDeltaY <= Config.MaxStepHeight + 0.01f)
                    {
                        Vector3 candidateStepPos = new(steppedHighPos.X, targetStepY, steppedHighPos.Z);
                        if (!IsCylinderPenetratingWall(candidateStepPos))
                        {
                            _position = candidateStepPos;
                            actualHorizVel = steppedVel;
                            IsGrounded = true;
                            if (supportTri is not null)
                            {
                                GroundSurfaceMetadata = supportTri.Metadata;
                                LastGroundSurfaceMetadata = supportTri.Metadata;
                                GroundMaterialId = supportTri.MaterialId;
                            }
                            return true;
                        }
                    }
                }
            }
        }

        _position = unsteppedPos;
        actualHorizVel = unsteppedVel;
        return false;
    }

    private bool TryFindLowObstacleTop(
        float cx,
        float cz,
        float feetY,
        float maxStepTopY,
        out float highestObstacleTopY,
        out CollisionTriangle? obstacleTri)
    {
        float minWalkableNormalY = MathF.Cos(Config.MaxSlopeDegrees * (MathF.PI / 180.0f));
        float radius = Config.Radius;
        float radiusSq = radius * radius;
        Vector2 centerXZ = new(cx, cz);

        highestObstacleTopY = feetY;
        obstacleTri = null;

        Span<Vector3> clipped3D = stackalloc Vector3[8];
        Span<Vector2> clipped2D = stackalloc Vector2[8];

        for (int i = 0; i < World.Triangles.Count; i++)
        {
            CollisionTriangle tri = World.Triangles[i];
            if (tri.Normal.Y >= minWalkableNormalY || tri.Metadata.IsCollisionDisabled())
            {
                continue;
            }

            if (tri.Bounds.Max.Y <= feetY + 0.005f || tri.Bounds.Max.Y > maxStepTopY + 0.005f ||
                tri.Bounds.Max.X < cx - radius || tri.Bounds.Min.X > cx + radius ||
                tri.Bounds.Max.Z < cz - radius || tri.Bounds.Min.Z > cz + radius)
            {
                continue;
            }

            int clipCount = CollisionMeshWorld.ClipTriangleToYSlab(tri.V0, tri.V1, tri.V2, feetY, maxStepTopY + 0.005f, clipped3D);
            if (clipCount == 0)
            {
                continue;
            }

            for (int c = 0; c < clipCount; c++)
            {
                clipped2D[c] = new Vector2(clipped3D[c].X, clipped3D[c].Z);
            }

            Vector2 closestXZ = CollisionMeshWorld.ClosestPointOnPolygon2D(centerXZ, clipped2D[..clipCount]);
            if (Vector2.DistanceSquared(centerXZ, closestXZ) <= radiusSq)
            {
                if (tri.Bounds.Max.Y > highestObstacleTopY)
                {
                    highestObstacleTopY = tri.Bounds.Max.Y;
                    obstacleTri = tri;
                }
            }
        }

        return obstacleTri is not null;
    }

    private void SimulateHorizontalSlide(
        Vector3 startPos,
        Vector3 desiredDeltaXZ,
        float dt,
        out Vector3 finalPos,
        out Vector3 finalVelXZ,
        out bool hitAnyWall,
        out CollisionTriangle? lastHitWallTri)
    {
        hitAnyWall = false;
        lastHitWallTri = null;

        float totalDist = desiredDeltaXZ.Length();
        float maxSubStep = System.Math.Max(0.05f, Config.Radius * 0.35f);
        int subSteps = System.Math.Clamp((int)MathF.Ceiling(totalDist / maxSubStep), 1, 16);

        Vector3 currentPos = startPos;
        Vector2 remainingDelta = new(desiredDeltaXZ.X, desiredDeltaXZ.Z);
        Vector2 currentVel = dt > 1e-6f ? remainingDelta / dt : Vector2.Zero;

        float minWalkableNormalY = MathF.Cos(Config.MaxSlopeDegrees * (MathF.PI / 180.0f));
        float radius = Config.Radius;
        float radiusSq = radius * radius;

        Span<Vector3> clipped3D = stackalloc Vector3[8];
        Span<Vector2> clipped2D = stackalloc Vector2[8];

        for (int step = 0; step < subSteps; step++)
        {
            Vector2 subStepStartXZ = new(currentPos.X, currentPos.Z);
            float fraction = 1.0f / (subSteps - step);
            Vector2 stepMove = remainingDelta * fraction;
            remainingDelta -= stepMove;

            Vector2 candidateXZ = subStepStartXZ + stepMove;
            float yMin = currentPos.Y + WallBottomClearance;
            float yMax = currentPos.Y + Config.Height - WallTopClearance;

            for (int iter = 0; iter < MaxSlideIterations; iter++)
            {
                bool hitThisIter = false;

                for (int tIdx = 0; tIdx < World.Triangles.Count; tIdx++)
                {
                    CollisionTriangle tri = World.Triangles[tIdx];
                    if (tri.Normal.Y >= minWalkableNormalY || tri.Metadata.IsCollisionDisabled())
                    {
                        continue;
                    }

                    // Broadphase AABB check against cylinder
                    if (tri.Bounds.Max.Y <= yMin || tri.Bounds.Min.Y >= yMax ||
                        tri.Bounds.Max.X < candidateXZ.X - radius || tri.Bounds.Min.X > candidateXZ.X + radius ||
                        tri.Bounds.Max.Z < candidateXZ.Y - radius || tri.Bounds.Min.Z > candidateXZ.Y + radius)
                    {
                        continue;
                    }

                    int clipCount = CollisionMeshWorld.ClipTriangleToYSlab(tri.V0, tri.V1, tri.V2, yMin, yMax, clipped3D);
                    if (clipCount == 0)
                    {
                        continue;
                    }

                    for (int c = 0; c < clipCount; c++)
                    {
                        clipped2D[c] = new Vector2(clipped3D[c].X, clipped3D[c].Z);
                    }

                    Vector2 closestXZ = CollisionMeshWorld.ClosestPointOnPolygon2D(candidateXZ, clipped2D[..clipCount]);
                    Vector2 diff = candidateXZ - closestXZ;
                    float distSq = diff.LengthSquared();

                    if (distSq < radiusSq - 1e-7f)
                    {
                        hitAnyWall = true;
                        hitThisIter = true;
                        lastHitWallTri = tri;

                        Vector2 triNormXZ = new(tri.Normal.X, tri.Normal.Z);
                        bool hasTriNormXZ = triNormXZ.LengthSquared() > 1e-6f;
                        if (hasTriNormXZ)
                        {
                            triNormXZ = Vector2.Normalize(triNormXZ);
                        }

                        Vector2 pushDir;
                        float penetration;

                        if (distSq > 1e-8f)
                        {
                            float dist = MathF.Sqrt(distSq);
                            pushDir = diff / dist;

                            // Ensure push direction does not push through the back of a wall the player approached from the front
                            if (hasTriNormXZ)
                            {
                                float startSide = Vector2.Dot(subStepStartXZ - closestXZ, triNormXZ);
                                if (startSide >= -0.02f && Vector2.Dot(pushDir, triNormXZ) < 0.0f)
                                {
                                    pushDir = triNormXZ;
                                    dist = Vector2.Dot(candidateXZ - closestXZ, triNormXZ);
                                }
                            }

                            penetration = radius - dist;
                        }
                        else
                        {
                            pushDir = hasTriNormXZ ? triNormXZ : new Vector2(0.0f, -1.0f);
                            penetration = radius;
                        }

                        if (penetration > 0.0f)
                        {
                            candidateXZ += pushDir * penetration;

                            // Slide remaining delta and velocity along the wall tangent
                            float remDot = Vector2.Dot(remainingDelta, pushDir);
                            if (remDot < 0.0f)
                            {
                                remainingDelta -= pushDir * remDot;
                            }

                            float velDot = Vector2.Dot(currentVel, pushDir);
                            if (velDot < 0.0f)
                            {
                                currentVel -= pushDir * velDot;
                            }
                        }
                    }
                }

                if (!hitThisIter)
                {
                    break;
                }
            }

            currentPos = new Vector3(candidateXZ.X, currentPos.Y, candidateXZ.Y);
        }

        finalPos = currentPos;
        finalVelXZ = new Vector3(currentVel.X, 0.0f, currentVel.Y);
    }

    private bool IsCylinderPenetratingWall(Vector3 pos)
    {
        float minWalkableNormalY = MathF.Cos(Config.MaxSlopeDegrees * (MathF.PI / 180.0f));
        float radius = System.Math.Max(0.01f, Config.Radius - 0.005f);
        float radiusSq = radius * radius;
        float yMin = pos.Y + WallBottomClearance;
        float yMax = pos.Y + Config.Height - WallTopClearance;
        Vector2 centerXZ = new(pos.X, pos.Z);

        Span<Vector3> clipped3D = stackalloc Vector3[8];
        Span<Vector2> clipped2D = stackalloc Vector2[8];

        for (int i = 0; i < World.Triangles.Count; i++)
        {
            CollisionTriangle tri = World.Triangles[i];
            if (tri.Normal.Y >= minWalkableNormalY || tri.Metadata.IsCollisionDisabled())
            {
                continue;
            }

            if (tri.Bounds.Max.Y <= yMin || tri.Bounds.Min.Y >= yMax ||
                tri.Bounds.Max.X < centerXZ.X - radius || tri.Bounds.Min.X > centerXZ.X + radius ||
                tri.Bounds.Max.Z < centerXZ.Y - radius || tri.Bounds.Min.Z > centerXZ.Y + radius)
            {
                continue;
            }

            int clipCount = CollisionMeshWorld.ClipTriangleToYSlab(tri.V0, tri.V1, tri.V2, yMin, yMax, clipped3D);
            if (clipCount == 0)
            {
                continue;
            }

            for (int c = 0; c < clipCount; c++)
            {
                clipped2D[c] = new Vector2(clipped3D[c].X, clipped3D[c].Z);
            }

            Vector2 closestXZ = CollisionMeshWorld.ClosestPointOnPolygon2D(centerXZ, clipped2D[..clipCount]);
            if (Vector2.DistanceSquared(centerXZ, closestXZ) < radiusSq)
            {
                return true;
            }
        }

        return false;
    }

    private void RefreshGroundState()
    {
        if (TryFindHighestFloor(_position.X, _position.Z, _position.Y - 0.06f, _position.Y + 0.06f, out float floorY, out CollisionTriangle? floorTri))
        {
            if (Velocity.Y <= 0.0f)
            {
                _position.Y = floorY;
            }
            IsGrounded = true;
            GroundSurfaceMetadata = floorTri!.Metadata;
            LastGroundSurfaceMetadata = floorTri.Metadata;
            GroundMaterialId = floorTri.MaterialId;
        }
        else
        {
            IsGrounded = false;
            GroundSurfaceMetadata = null;
            GroundMaterialId = null;
        }
    }

    private bool TryFindHighestFloor(
        float cx,
        float cz,
        float minY,
        float maxY,
        out float bestFloorY,
        out CollisionTriangle? bestTri)
    {
        float minWalkableNormalY = MathF.Cos(Config.MaxSlopeDegrees * (MathF.PI / 180.0f));
        float supportRadius = System.Math.Max(0.01f, Config.Radius - 0.01f);
        float supportRadiusSq = supportRadius * supportRadius;
        Vector2 centerXZ = new(cx, cz);

        bestFloorY = float.NegativeInfinity;
        float bestDistSq = float.MaxValue;
        bestTri = null;

        Span<Vector2> tri2D = stackalloc Vector2[3];

        for (int i = 0; i < World.Triangles.Count; i++)
        {
            CollisionTriangle tri = World.Triangles[i];
            if (tri.Normal.Y < minWalkableNormalY || tri.Metadata.IsCollisionDisabled())
            {
                continue;
            }

            if (tri.Bounds.Max.Y < minY || tri.Bounds.Min.Y > maxY ||
                tri.Bounds.Max.X < cx - supportRadius || tri.Bounds.Min.X > cx + supportRadius ||
                tri.Bounds.Max.Z < cz - supportRadius || tri.Bounds.Min.Z > cz + supportRadius)
            {
                continue;
            }

            tri2D[0] = new Vector2(tri.V0.X, tri.V0.Z);
            tri2D[1] = new Vector2(tri.V1.X, tri.V1.Z);
            tri2D[2] = new Vector2(tri.V2.X, tri.V2.Z);

            Vector2 closestXZ = CollisionMeshWorld.ClosestPointOnPolygon2D(centerXZ, tri2D);
            float distSq = Vector2.DistanceSquared(centerXZ, closestXZ);
            if (distSq > supportRadiusSq)
            {
                continue;
            }

            float surfaceY = tri.V0.Y - (((tri.Normal.X * (closestXZ.X - tri.V0.X)) + (tri.Normal.Z * (closestXZ.Y - tri.V0.Z))) / tri.Normal.Y);
            surfaceY = System.Math.Clamp(surfaceY, tri.Bounds.Min.Y, tri.Bounds.Max.Y);

            if (surfaceY >= minY && surfaceY <= maxY)
            {
                if (surfaceY > bestFloorY + 1e-4f ||
                    (MathF.Abs(surfaceY - bestFloorY) <= 1e-4f && distSq < bestDistSq))
                {
                    bestFloorY = surfaceY;
                    bestDistSq = distSq;
                    bestTri = tri;
                }
            }
        }

        return bestTri is not null;
    }

    private bool TryFindLowestCeiling(float cx, float cz, float minY, float maxY, out float lowestCeilingY)
    {
        float supportRadius = System.Math.Max(0.01f, Config.Radius - 0.02f);
        float supportRadiusSq = supportRadius * supportRadius;
        Vector2 centerXZ = new(cx, cz);

        lowestCeilingY = float.PositiveInfinity;
        bool found = false;

        Span<Vector2> tri2D = stackalloc Vector2[3];

        for (int i = 0; i < World.Triangles.Count; i++)
        {
            CollisionTriangle tri = World.Triangles[i];
            if (tri.Normal.Y > -0.35f || tri.Metadata.IsCollisionDisabled())
            {
                continue;
            }

            if (tri.Bounds.Max.Y < minY || tri.Bounds.Min.Y > maxY ||
                tri.Bounds.Max.X < cx - supportRadius || tri.Bounds.Min.X > cx + supportRadius ||
                tri.Bounds.Max.Z < cz - supportRadius || tri.Bounds.Min.Z > cz + supportRadius)
            {
                continue;
            }

            tri2D[0] = new Vector2(tri.V0.X, tri.V0.Z);
            tri2D[1] = new Vector2(tri.V1.X, tri.V1.Z);
            tri2D[2] = new Vector2(tri.V2.X, tri.V2.Z);

            Vector2 closestXZ = CollisionMeshWorld.ClosestPointOnPolygon2D(centerXZ, tri2D);
            float distSq = Vector2.DistanceSquared(centerXZ, closestXZ);
            if (distSq > supportRadiusSq)
            {
                continue;
            }

            float ceilingY = tri.V0.Y - (((tri.Normal.X * (closestXZ.X - tri.V0.X)) + (tri.Normal.Z * (closestXZ.Y - tri.V0.Z))) / tri.Normal.Y);
            ceilingY = System.Math.Clamp(ceilingY, tri.Bounds.Min.Y, tri.Bounds.Max.Y);

            if (ceilingY >= minY - 0.02f && ceilingY <= maxY + 0.02f && ceilingY < lowestCeilingY)
            {
                lowestCeilingY = ceilingY;
                found = true;
            }
        }

        return found;
    }

    private void RefreshTriggers(bool allowTeleport)
    {
        _activeTriggers.Clear();
        _activeTriggerIds.Clear();

        BoundingBox3D playerBounds = GetPlayerBounds();

        for (int i = 0; i < World.Triggers.Count; i++)
        {
            CollisionTriggerVolume trigger = World.Triggers[i];
            if (trigger.OneShot && _consumedOneShotTriggers.Contains(trigger.EntityId))
            {
                continue;
            }

            if (playerBounds.Intersects(trigger.Bounds))
            {
                _activeTriggers.Add(trigger);
                _activeTriggerIds.Add(trigger.EntityId);

                if (trigger.OneShot)
                {
                    _consumedOneShotTriggers.Add(trigger.EntityId);
                }

                bool isPortal = trigger.ClassName.Equals("portal", StringComparison.OrdinalIgnoreCase);
                if (isPortal &&
                    allowTeleport &&
                    !string.IsNullOrWhiteSpace(trigger.TargetId) &&
                    !_cooldownPortalIds.Contains(trigger.EntityId) &&
                    World.Destinations.TryGetValue(trigger.TargetId, out TeleportDestination? dest) &&
                    dest is not null)
                {
                    _position = dest.Position;
                    if (dest.YawDegrees.HasValue)
                    {
                        YawDegrees = dest.YawDegrees.Value;
                    }

                    LastTeleportedPortalId = trigger.EntityId;
                    _cooldownPortalIds.Add(trigger.EntityId);
                    _cooldownPortalIds.Add(dest.TargetId);

                    RefreshGroundState();
                    playerBounds = GetPlayerBounds();
                }
            }
        }

        // Remove cooldown for any portal the player is no longer intersecting
        _cooldownPortalIds.RemoveWhere(id =>
        {
            for (int i = 0; i < World.Triggers.Count; i++)
            {
                if (World.Triggers[i].EntityId.Equals(id, StringComparison.OrdinalIgnoreCase) &&
                    playerBounds.Intersects(World.Triggers[i].Bounds))
                {
                    return false;
                }
            }
            return true;
        });
    }
}
