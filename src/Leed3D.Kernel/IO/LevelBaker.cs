using System.Numerics;
using Leed3D.Kernel.Entities;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Math;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Texturing;
using Leed3D.Runtime;
using Leed3D.Runtime.Metadata;

namespace Leed3D.Kernel.IO;

/// <summary>
/// Pre-bakes editable scenes into game-ready zero-triangulation runtime layers
/// (<see cref="BakedRuntimeLayerData"/>) with material-grouped render submeshes,
/// pre-filtered collision triangles, and target coordinate system conversions.
/// </summary>
public static class LevelBaker
{
    /// <summary>
    /// Bakes the given <paramref name="scene"/> into a <see cref="BakedRuntimeLayerData"/>.
    /// </summary>
    /// <param name="scene">The editor scene to bake.</param>
    /// <param name="rightHanded">
    /// When <c>true</c>, coordinates are pre-converted to Right-Handed space (+X Right, +Y Up, -Z Forward)
    /// and triangle index winding is flipped for OpenGL/Raylib/glTF runtimes.
    /// When <c>false</c>, data remains in Leed3D Left-Handed space (+X Right, +Y Up, +Z Away).
    /// </param>
    public static BakedRuntimeLayerData Bake(EditorScene scene, bool rightHanded = false)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var submeshByMaterial = new Dictionary<string, BakedSubmeshDto>(StringComparer.OrdinalIgnoreCase);
        var collisionTriangles = new List<BakedCollisionTriangleDto>();

        Vector3 minBounds = new(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 maxBounds = new(float.MinValue, float.MinValue, float.MinValue);
        bool hasGeometry = false;

        // 1. Bake Render Submeshes & Collision Triangles from visible objects
        for (int objIdx = 0; objIdx < scene.Objects.Count; objIdx++)
        {
            SceneObject obj = scene.Objects[objIdx];
            if (!obj.Visible || obj.Mesh is null || obj.Mesh.FaceCount == 0)
            {
                continue;
            }

            EditableMesh mesh = obj.Mesh;
            bool isObjCollisionDisabled = obj.Metadata.IsCollisionDisabled();

            for (int fIdx = 0; fIdx < mesh.Faces.Count; fIdx++)
            {
                PolygonFace face = mesh.Faces[fIdx];
                if (face.VertexIds.Count < 3)
                {
                    continue;
                }

                IReadOnlyList<(int V0, int V1, int V2)> tris = PolygonTriangulator.TriangulateFace(face, mesh);
                if (tris.Count == 0)
                {
                    continue;
                }

                // Get or create render submesh for this face's material
                string matId = string.IsNullOrWhiteSpace(face.MaterialId) ? PolygonFace.DefaultMaterialId : face.MaterialId;
                if (!submeshByMaterial.TryGetValue(matId, out BakedSubmeshDto? submesh))
                {
                    submesh = new BakedSubmeshDto(matId);
                    submeshByMaterial[matId] = submesh;
                }

                Vector3 localNormal = face.ComputeNormal(mesh);
                Vector3 worldNormal = Vector3.Normalize(obj.Transform.TransformDirectionToWorld(localNormal));
                IReadOnlyList<Vector2> faceUvs = UvProjectionEngine.ComputeFaceUvs(face, mesh, obj.Transform);

                bool isFaceCollisionDisabled = isObjCollisionDisabled || face.Metadata.IsCollisionDisabled();
                ElementMetadata? mergedCollisionMeta = null;
                if (!isFaceCollisionDisabled)
                {
                    mergedCollisionMeta = obj.Metadata.Clone();
                    mergedCollisionMeta.MergeFrom(face.Metadata);
                }

                for (int tIdx = 0; tIdx < tris.Count; tIdx++)
                {
                    var (v0Id, v1Id, v2Id) = tris[tIdx];

                    MeshVertex? mv0 = mesh.GetVertex(v0Id);
                    MeshVertex? mv1 = mesh.GetVertex(v1Id);
                    MeshVertex? mv2 = mesh.GetVertex(v2Id);
                    if (mv0 is null || mv1 is null || mv2 is null)
                    {
                        continue;
                    }

                    int idx0 = face.VertexIds.IndexOf(v0Id);
                    int idx1 = face.VertexIds.IndexOf(v1Id);
                    int idx2 = face.VertexIds.IndexOf(v2Id);

                    Vector2 uv0 = idx0 >= 0 && idx0 < faceUvs.Count ? faceUvs[idx0] : Vector2.Zero;
                    Vector2 uv1 = idx1 >= 0 && idx1 < faceUvs.Count ? faceUvs[idx1] : Vector2.Zero;
                    Vector2 uv2 = idx2 >= 0 && idx2 < faceUvs.Count ? faceUvs[idx2] : Vector2.Zero;

                    Vector3 p0 = obj.Transform.TransformPointToWorld(mv0.Position);
                    Vector3 p1 = obj.Transform.TransformPointToWorld(mv1.Position);
                    Vector3 p2 = obj.Transform.TransformPointToWorld(mv2.Position);

                    if (rightHanded)
                    {
                        Vector3 p0Rh = CoordSpace.ToRightHanded(p0);
                        Vector3 p1Rh = CoordSpace.ToRightHanded(p1);
                        Vector3 p2Rh = CoordSpace.ToRightHanded(p2);
                        Vector3 nRh = CoordSpace.ToRightHanded(worldNormal);

                        UpdateBounds(ref minBounds, ref maxBounds, ref hasGeometry, p0Rh);
                        UpdateBounds(ref minBounds, ref maxBounds, ref hasGeometry, p1Rh);
                        UpdateBounds(ref minBounds, ref maxBounds, ref hasGeometry, p2Rh);

                        // Winding flip for right-handed space: (0, 1, 2) -> (0, 2, 1)
                        int baseIdx = submesh.Positions.Count;
                        submesh.Positions.Add(p0Rh);
                        submesh.Positions.Add(p2Rh);
                        submesh.Positions.Add(p1Rh);

                        submesh.Normals.Add(nRh);
                        submesh.Normals.Add(nRh);
                        submesh.Normals.Add(nRh);

                        submesh.UVs.Add(uv0);
                        submesh.UVs.Add(uv2);
                        submesh.UVs.Add(uv1);

                        submesh.Indices.Add(baseIdx);
                        submesh.Indices.Add(baseIdx + 1);
                        submesh.Indices.Add(baseIdx + 2);

                        submesh.FaceSourceIds.Add(face.Id);

                        if (!isFaceCollisionDisabled && mergedCollisionMeta is not null)
                        {
                            collisionTriangles.Add(new BakedCollisionTriangleDto(
                                p0Rh,
                                p2Rh,
                                p1Rh,
                                nRh,
                                matId,
                                mergedCollisionMeta));
                        }
                    }
                    else
                    {
                        UpdateBounds(ref minBounds, ref maxBounds, ref hasGeometry, p0);
                        UpdateBounds(ref minBounds, ref maxBounds, ref hasGeometry, p1);
                        UpdateBounds(ref minBounds, ref maxBounds, ref hasGeometry, p2);

                        int baseIdx = submesh.Positions.Count;
                        submesh.Positions.Add(p0);
                        submesh.Positions.Add(p1);
                        submesh.Positions.Add(p2);

                        submesh.Normals.Add(worldNormal);
                        submesh.Normals.Add(worldNormal);
                        submesh.Normals.Add(worldNormal);

                        submesh.UVs.Add(uv0);
                        submesh.UVs.Add(uv1);
                        submesh.UVs.Add(uv2);

                        submesh.Indices.Add(baseIdx);
                        submesh.Indices.Add(baseIdx + 1);
                        submesh.Indices.Add(baseIdx + 2);

                        submesh.FaceSourceIds.Add(face.Id);

                        if (!isFaceCollisionDisabled && mergedCollisionMeta is not null)
                        {
                            collisionTriangles.Add(new BakedCollisionTriangleDto(
                                p0,
                                p1,
                                p2,
                                worldNormal,
                                matId,
                                mergedCollisionMeta));
                        }
                    }
                }
            }
        }

        // 2. Bake Entities with converted coordinates
        var bakedEntities = new List<LevelEntityDto>(scene.Entities.Count);
        for (int i = 0; i < scene.Entities.Count; i++)
        {
            LevelEntity ent = scene.Entities[i];
            if (rightHanded)
            {
                Vector3 posRh = CoordSpace.ToRightHanded(ent.Transform.Position);
                Quaternion rotRh = CoordSpace.ToRightHandedRotation(ent.Transform.GetRotationQuaternion());
                var tempTransform = new Transform3D();
                tempTransform.SetRotationFromQuaternion(rotRh);
                Vector3 eulerRh = tempTransform.RotationEulerDegrees;

                UpdateBounds(ref minBounds, ref maxBounds, ref hasGeometry, posRh);

                bakedEntities.Add(new LevelEntityDto(
                    ent.Id,
                    ent.ClassName,
                    new TransformDto(posRh, eulerRh, ent.Transform.Scale),
                    ent.Properties.Clone(),
                    ent.Visible,
                    ent.Locked));
            }
            else
            {
                UpdateBounds(ref minBounds, ref maxBounds, ref hasGeometry, ent.Transform.Position);

                bakedEntities.Add(new LevelEntityDto(
                    ent.Id,
                    ent.ClassName,
                    new TransformDto(ent.Transform.Position, ent.Transform.RotationEulerDegrees, ent.Transform.Scale),
                    ent.Properties.Clone(),
                    ent.Visible,
                    ent.Locked));
            }
        }

        // 3. Fallback bounds if no geometry or entities exist
        if (!hasGeometry)
        {
            minBounds = Vector3.Zero;
            maxBounds = Vector3.Zero;
        }

        return new BakedRuntimeLayerData
        {
            WorldBounds = new BoundingBoxDto(minBounds, maxBounds),
            RenderSubmeshes = submeshByMaterial.Values.ToList(),
            CollisionTriangles = collisionTriangles,
            Entities = bakedEntities
        };
    }

    private static void UpdateBounds(ref Vector3 min, ref Vector3 max, ref bool hasGeometry, Vector3 p)
    {
        hasGeometry = true;
        min = Vector3.Min(min, p);
        max = Vector3.Max(max, p);
    }
}
