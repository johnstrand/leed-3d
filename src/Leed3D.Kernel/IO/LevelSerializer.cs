using System.Text.Json;
using Leed3D.Kernel.Entities;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Texturing;
using Leed3D.Runtime;

namespace Leed3D.Kernel.IO;

/// <summary>
/// Lossless serializer and deserializer for <see cref="EditorScene"/> and Dual-Layer <c>.level.json</c> documents.
/// </summary>
public static class LevelSerializer
{
    /// <summary>
    /// Saves an <see cref="EditorScene"/> to a <c>.level.json</c> file with both lossless editor topology
    /// and pre-baked runtime buffers.
    /// </summary>
    public static void Save(EditorScene scene, string filePath, bool bakeRightHandedRuntime = false, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        LeedLevelDocument document = CreateDocumentFromEditorScene(scene, bakeRightHandedRuntime);
        LeedLevelLoader.Save(document, filePath, options);
    }

    /// <summary>
    /// Serializes an <see cref="EditorScene"/> directly to a JSON string.
    /// </summary>
    public static string SaveToJson(EditorScene scene, bool bakeRightHandedRuntime = false, JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        LeedLevelDocument document = CreateDocumentFromEditorScene(scene, bakeRightHandedRuntime);
        return LeedLevelLoader.SaveToJson(document, options);
    }

    /// <summary>
    /// Loads an <see cref="EditorScene"/> with 100% topological and metadata fidelity from a <c>.level.json</c> file.
    /// </summary>
    public static EditorScene LoadEditorScene(string filePath, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        LeedLevelDocument document = LeedLevelLoader.Load(filePath, options);
        return CreateEditorSceneFromDocument(document);
    }

    /// <summary>
    /// Loads an <see cref="EditorScene"/> with 100% topological and metadata fidelity from a JSON string.
    /// </summary>
    public static EditorScene LoadEditorSceneFromJson(string json, JsonSerializerOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        LeedLevelDocument document = LeedLevelLoader.LoadFromJson(json, options);
        return CreateEditorSceneFromDocument(document);
    }

    /// <summary>
    /// Constructs a dual-layer <see cref="LeedLevelDocument"/> from an <see cref="EditorScene"/>.
    /// </summary>
    public static LeedLevelDocument CreateDocumentFromEditorScene(EditorScene scene, bool bakeRightHandedRuntime = false)
    {
        ArgumentNullException.ThrowIfNull(scene);

        var editorLayer = new EditorLayerData();

        // 1. Serialize Editor Objects
        for (int i = 0; i < scene.Objects.Count; i++)
        {
            SceneObject obj = scene.Objects[i];
            var objDto = new EditorObjectDto(
                obj.Id,
                obj.Name,
                new TransformDto(obj.Transform.Position, obj.Transform.RotationEulerDegrees, obj.Transform.Scale),
                null,
                obj.Metadata.Clone(),
                obj.Visible,
                obj.Locked);

            if (obj.Mesh is not null)
            {
                var meshDto = new EditorMeshDto();

                for (int vIdx = 0; vIdx < obj.Mesh.Vertices.Count; vIdx++)
                {
                    MeshVertex v = obj.Mesh.Vertices[vIdx];
                    meshDto.Vertices.Add(new EditorVertexDto(v.Id, v.Position, v.Metadata.Clone()));
                }

                for (int fIdx = 0; fIdx < obj.Mesh.Faces.Count; fIdx++)
                {
                    PolygonFace f = obj.Mesh.Faces[fIdx];
                    var uvDto = new FaceUvMappingDto(
                        f.UvMapping.UAxis,
                        f.UvMapping.VAxis,
                        f.UvMapping.Offset,
                        f.UvMapping.Scale,
                        f.UvMapping.RotationDegrees,
                        f.UvMapping.TextureLock);

                    meshDto.Faces.Add(new PolygonFaceDto(
                        f.Id,
                        f.VertexIds,
                        f.MaterialId,
                        uvDto,
                        f.Metadata.Clone()));
                }

                foreach (var kvp in obj.Mesh.EdgeMetadata)
                {
                    if (kvp.Value.Count > 0)
                    {
                        meshDto.Edges.Add(new EdgeMetadataDto(kvp.Key.VMin, kvp.Key.VMax, kvp.Value.Clone()));
                    }
                }

                objDto.Mesh = meshDto;
            }

            editorLayer.Objects.Add(objDto);
        }

        // 2. Serialize Editor Entities
        for (int i = 0; i < scene.Entities.Count; i++)
        {
            LevelEntity ent = scene.Entities[i];
            editorLayer.Entities.Add(new LevelEntityDto(
                ent.Id,
                ent.ClassName,
                new TransformDto(ent.Transform.Position, ent.Transform.RotationEulerDegrees, ent.Transform.Scale),
                ent.Properties.Clone(),
                ent.Visible,
                ent.Locked));
        }

        // 3. Bake Runtime Layer
        BakedRuntimeLayerData bakedLayer = LevelBaker.Bake(scene, bakeRightHandedRuntime);

        return new LeedLevelDocument
        {
            FormatVersion = LeedLevelDocument.CurrentFormatVersion,
            Name = string.IsNullOrWhiteSpace(scene.Name) ? "Untitled" : scene.Name,
            CoordinateSystem = bakeRightHandedRuntime
                ? LeedLevelDocument.RightHandedCoordinateSystem
                : LeedLevelDocument.LeftHandedCoordinateSystem,
            Editor = editorLayer,
            Baked = bakedLayer
        };
    }

    /// <summary>
    /// Reconstructs an <see cref="EditorScene"/> with 100% fidelity from a <see cref="LeedLevelDocument"/>.
    /// </summary>
    public static EditorScene CreateEditorSceneFromDocument(LeedLevelDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.FormatVersion < 1 || document.FormatVersion > LeedLevelDocument.CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"Unsupported level format version: {document.FormatVersion}. Expected version {LeedLevelDocument.CurrentFormatVersion}.");
        }

        if (document.Editor is null || document.Editor.Objects is null || document.Editor.Entities is null)
        {
            throw new InvalidDataException("Invalid level file: missing 'editor' layer data.");
        }

        var scene = new EditorScene(string.IsNullOrWhiteSpace(document.Name) ? "Untitled" : document.Name);
        var seenObjectIds = new HashSet<int>();

        // 1. Reconstruct Objects
        for (int i = 0; i < document.Editor.Objects.Count; i++)
        {
            EditorObjectDto? objDto = document.Editor.Objects[i];
            if (objDto is null)
            {
                throw new InvalidDataException($"Invalid level file: object at index {i} is null.");
            }

            if (objDto.Transform is null)
            {
                throw new InvalidDataException($"Invalid level file: object '{objDto.Name}' (ID {objDto.Id}) is missing a transform.");
            }

            if (!seenObjectIds.Add(objDto.Id))
            {
                throw new InvalidDataException($"Invalid level file: duplicate object ID {objDto.Id}.");
            }

            EditableMesh? mesh = null;

            if (objDto.Mesh is not null)
            {
                if (objDto.Mesh.Vertices is null || objDto.Mesh.Faces is null || objDto.Mesh.Edges is null)
                {
                    throw new InvalidDataException($"Invalid level file: object '{objDto.Name}' (ID {objDto.Id}) has an invalid mesh definition.");
                }

                mesh = new EditableMesh();
                var seenVertexIds = new HashSet<int>();

                for (int vIdx = 0; vIdx < objDto.Mesh.Vertices.Count; vIdx++)
                {
                    EditorVertexDto? vDto = objDto.Mesh.Vertices[vIdx];
                    if (vDto is null)
                    {
                        throw new InvalidDataException($"Invalid level file: vertex at index {vIdx} in object '{objDto.Name}' is null.");
                    }

                    if (!seenVertexIds.Add(vDto.Id))
                    {
                        throw new InvalidDataException($"Invalid level file: duplicate vertex ID {vDto.Id} in object '{objDto.Name}'.");
                    }

                    if (!float.IsFinite(vDto.Position.X) || !float.IsFinite(vDto.Position.Y) || !float.IsFinite(vDto.Position.Z))
                    {
                        throw new InvalidDataException($"Invalid level file: vertex {vDto.Id} in object '{objDto.Name}' has non-finite coordinates.");
                    }

                    mesh.AddVertexWithId(vDto.Id, vDto.Position, vDto.Metadata?.Clone());
                }

                var seenFaceIds = new HashSet<int>();

                for (int fIdx = 0; fIdx < objDto.Mesh.Faces.Count; fIdx++)
                {
                    PolygonFaceDto? fDto = objDto.Mesh.Faces[fIdx];
                    if (fDto is null || fDto.VertexIds is null || fDto.VertexIds.Count < 3)
                    {
                        throw new InvalidDataException($"Invalid level file: face at index {fIdx} in object '{objDto.Name}' must have at least 3 vertices.");
                    }

                    if (!seenFaceIds.Add(fDto.Id))
                    {
                        throw new InvalidDataException($"Invalid level file: duplicate face ID {fDto.Id} in object '{objDto.Name}'.");
                    }

                    for (int vRefIdx = 0; vRefIdx < fDto.VertexIds.Count; vRefIdx++)
                    {
                        int vId = fDto.VertexIds[vRefIdx];
                        if (!seenVertexIds.Contains(vId))
                        {
                            throw new InvalidDataException(
                                $"Invalid level file: face {fDto.Id} in object '{objDto.Name}' references non-existent vertex ID {vId}.");
                        }
                    }

                    FaceUvMappingDto uvDto = fDto.UvMapping ?? new FaceUvMappingDto();
                    var uvMapping = new FaceUvMapping(
                        uvDto.UAxis,
                        uvDto.VAxis,
                        uvDto.Offset,
                        uvDto.Scale,
                        uvDto.RotationDegrees,
                        uvDto.TextureLock);

                    mesh.AddFaceWithId(
                        fDto.Id,
                        fDto.VertexIds,
                        fDto.MaterialId,
                        uvMapping,
                        fDto.Metadata?.Clone());

                    // Guarantee FaceUvMapping parameters are exact regardless of initial auto-alignment
                    PolygonFace? createdFace = mesh.GetFace(fDto.Id);
                    if (createdFace is not null)
                    {
                        createdFace.UvMapping = uvMapping;
                    }
                }

                for (int eIdx = 0; eIdx < objDto.Mesh.Edges.Count; eIdx++)
                {
                    EdgeMetadataDto? eDto = objDto.Mesh.Edges[eIdx];
                    if (eDto is null || eDto.V0 == eDto.V1)
                    {
                        continue;
                    }

                    var ek = new EdgeKey(eDto.V0, eDto.V1);
                    if (eDto.Metadata is not null)
                    {
                        mesh.EdgeMetadata[ek] = eDto.Metadata.Clone();
                    }
                }
            }

            var transform = new Transform3D(
                objDto.Transform.Position,
                objDto.Transform.RotationEulerDegrees,
                objDto.Transform.Scale);

            var sceneObj = new SceneObject(
                objDto.Id,
                string.IsNullOrWhiteSpace(objDto.Name) ? "Object" : objDto.Name,
                mesh,
                transform,
                objDto.Metadata?.Clone())
            {
                Visible = objDto.Visible,
                Locked = objDto.Locked
            };

            scene.AddObject(sceneObj);
        }

        var seenEntityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 2. Reconstruct Entities
        for (int i = 0; i < document.Editor.Entities.Count; i++)
        {
            LevelEntityDto? entDto = document.Editor.Entities[i];
            if (entDto is null || string.IsNullOrWhiteSpace(entDto.Id) || string.IsNullOrWhiteSpace(entDto.ClassName))
            {
                throw new InvalidDataException($"Invalid level file: entity at index {i} is missing an ID or className.");
            }

            if (entDto.Transform is null)
            {
                throw new InvalidDataException($"Invalid level file: entity '{entDto.Id}' is missing a transform.");
            }

            if (!seenEntityIds.Add(entDto.Id))
            {
                throw new InvalidDataException($"Invalid level file: duplicate entity ID '{entDto.Id}'.");
            }

            var transform = new Transform3D(
                entDto.Transform.Position,
                entDto.Transform.RotationEulerDegrees,
                entDto.Transform.Scale);

            var ent = new LevelEntity(
                entDto.Id,
                entDto.ClassName,
                transform,
                entDto.Properties?.Clone())
            {
                Visible = entDto.Visible,
                Locked = entDto.Locked
            };

            scene.AddEntity(ent);
        }

        return scene;
    }
}
