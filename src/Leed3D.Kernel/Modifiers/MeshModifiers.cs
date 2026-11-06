using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Selection;
using Leed3D.Kernel.Texturing;

namespace Leed3D.Kernel.Modifiers;

/// <summary>
/// Headless geometry duplication and repetition tools:
/// Quick Clone (Shift+D), Axis Mirror (X/Y/Z) with winding correction and seam welding,
/// Linear Array, and Radial Array at both sub-mesh and scene-object levels.
/// </summary>
public static class MeshModifiers
{
    /// <summary>
    /// Deep-clones the specified SceneObjects in <paramref name="scene"/>, assigns "_Copy" names,
    /// and optionally selects the new clones in <paramref name="selection"/>.
    /// Returns the list of newly created clones.
    /// </summary>
    public static IReadOnlyList<SceneObject> CloneObjects(
        EditorScene scene,
        IReadOnlyCollection<int> objectIds,
        SelectionManager? selection = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(objectIds);

        if (objectIds.Count == 0)
        {
            return [];
        }

        var clones = new List<SceneObject>(objectIds.Count);
        foreach (int id in objectIds)
        {
            SceneObject? original = scene.GetObject(id);
            if (original is not null)
            {
                SceneObject clone = scene.AddClonedObject(original, $"{original.Name}_Copy");
                clones.Add(clone);
            }
        }

        if (selection is not null && clones.Count > 0)
        {
            selection.SelectMultipleObjects(clones.Select(c => c.Id));
        }

        return clones;
    }

    /// <summary>
    /// Duplicates the specified faces and their referenced vertices within <paramref name="mesh"/>,
    /// rewiring the duplicated faces to share newly cloned vertices, and optionally selects the new faces
    /// in <paramref name="selection"/>.
    /// Returns the list of new face IDs.
    /// </summary>
    public static IReadOnlyList<int> CloneFaces(
        EditableMesh mesh,
        IReadOnlyCollection<int> faceIds,
        SelectionManager? selection = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(faceIds);

        if (faceIds.Count == 0)
        {
            return [];
        }

        var targetFaces = new List<PolygonFace>();
        var seenFaceIds = new HashSet<int>();

        foreach (int id in faceIds)
        {
            if (seenFaceIds.Add(id))
            {
                PolygonFace? face = mesh.GetFace(id);
                if (face is not null && face.VertexIds.Count >= 3)
                {
                    targetFaces.Add(face);
                }
            }
        }

        if (targetFaces.Count == 0)
        {
            return [];
        }

        var oldToNewVertexId = new Dictionary<int, int>();
        foreach (PolygonFace face in targetFaces)
        {
            foreach (int vId in face.VertexIds)
            {
                if (!oldToNewVertexId.ContainsKey(vId))
                {
                    MeshVertex? v = mesh.GetVertex(vId);
                    if (v is not null)
                    {
                        int newVId = mesh.AddVertex(v.Position);
                        oldToNewVertexId[vId] = newVId;
                        if (mesh.GetVertex(newVId) is { } newVert && (v.Metadata.Properties.Count > 0 || v.Metadata.Tags.Count > 0))
                        {
                            newVert.Metadata = v.Metadata.Clone();
                        }
                    }
                }
            }
        }

        var newFaceIds = new List<int>(targetFaces.Count);
        foreach (PolygonFace face in targetFaces)
        {
            var newLoop = face.VertexIds.Select(id => oldToNewVertexId[id]).ToList();
            int newFaceId = mesh.AddFace(newLoop, face.MaterialId, face.UvMapping);
            if (mesh.GetFace(newFaceId) is { } newFace && (face.Metadata.Properties.Count > 0 || face.Metadata.Tags.Count > 0))
            {
                newFace.Metadata = face.Metadata.Clone();
            }
            newFaceIds.Add(newFaceId);
        }

        mesh.ValidateAndRepair();

        if (selection is not null)
        {
            selection.SelectMultipleFaces(newFaceIds);
        }

        return newFaceIds;
    }

    /// <summary>
    /// Reflects <paramref name="mesh"/> across <paramref name="planeCoordinate"/> along <paramref name="axis"/>.
    /// Automatically reverses face winding so surface normals continue pointing outward.
    /// When <paramref name="keepOriginal"/> is true (symmetric modeling), duplicates existing geometry and
    /// welds vertices lying on the mirror plane within <paramref name="weldThreshold"/>.
    /// </summary>
    public static void MirrorMesh(
        EditableMesh mesh,
        Axis3D axis,
        float planeCoordinate = 0f,
        bool keepOriginal = false,
        float weldThreshold = 0.001f)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        if (mesh.VertexCount == 0)
        {
            return;
        }

        if (!keepOriginal)
        {
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                MeshVertex v = mesh.Vertices[i];
                float coord = GetAxisCoord(v.Position, axis);
                float mirroredCoord = 2.0f * planeCoordinate - coord;
                v.Position = SetAxisCoord(v.Position, axis, mirroredCoord);
            }

            for (int i = 0; i < mesh.Faces.Count; i++)
            {
                mesh.Faces[i].VertexIds.Reverse();
            }

            mesh.InvalidateTopology();
            return;
        }

        // keepOriginal == true (symmetric modeling)
        var originalVertices = mesh.Vertices.ToList();
        var originalFaces = mesh.Faces.ToList();
        var oldToMirroredVertexId = new Dictionary<int, int>();

        for (int i = 0; i < originalVertices.Count; i++)
        {
            MeshVertex v = originalVertices[i];
            float coord = GetAxisCoord(v.Position, axis);
            float distToPlane = MathF.Abs(coord - planeCoordinate);

            if (weldThreshold > 0.0f && distToPlane <= weldThreshold)
            {
                // Snap position to mirror plane so seam is perfectly planar and share with mirrored faces
                v.Position = SetAxisCoord(v.Position, axis, planeCoordinate);
                oldToMirroredVertexId[v.Id] = v.Id;
            }
            else
            {
                float mirroredCoord = 2.0f * planeCoordinate - coord;
                Vector3 mirroredPos = SetAxisCoord(v.Position, axis, mirroredCoord);
                int newVId = mesh.AddVertex(mirroredPos);
                oldToMirroredVertexId[v.Id] = newVId;
                if (mesh.GetVertex(newVId) is { } newVert && (v.Metadata.Properties.Count > 0 || v.Metadata.Tags.Count > 0))
                {
                    newVert.Metadata = v.Metadata.Clone();
                }
            }
        }

        for (int i = 0; i < originalFaces.Count; i++)
        {
            PolygonFace face = originalFaces[i];
            var mirroredLoop = face.VertexIds.Select(id => oldToMirroredVertexId[id]).Reverse().ToList();
            int newFId = mesh.AddFace(mirroredLoop, face.MaterialId);
            if (mesh.GetFace(newFId) is { } newFace && (face.Metadata.Properties.Count > 0 || face.Metadata.Tags.Count > 0))
            {
                newFace.Metadata = face.Metadata.Clone();
            }
        }

        var oldEdges = mesh.EdgeMetadata.ToList();
        foreach (var (edge, meta) in oldEdges)
        {
            if (oldToMirroredVertexId.TryGetValue(edge.VMin, out int mMin) &&
                oldToMirroredVertexId.TryGetValue(edge.VMax, out int mMax) &&
                mMin != mMax)
            {
                mesh.GetOrCreateEdgeMetadata(new EdgeKey(mMin, mMax)).MergeFrom(meta);
            }
        }

        mesh.ValidateAndRepair();
    }

    /// <summary>
    /// Mirrors a single SceneObject across <paramref name="worldPlaneCoordinate"/> along <paramref name="axis"/>.
    /// If <paramref name="keepOriginal"/> is true, clones the object and mirrors the clone.
    /// </summary>
    public static SceneObject? MirrorObject(
        EditorScene scene,
        int objectId,
        Axis3D axis,
        float worldPlaneCoordinate = 0f,
        bool keepOriginal = false,
        float weldThreshold = 0.001f)
    {
        ArgumentNullException.ThrowIfNull(scene);

        SceneObject? obj = scene.GetObject(objectId);
        if (obj is null)
        {
            return null;
        }

        SceneObject target = keepOriginal ? scene.AddClonedObject(obj, $"{obj.Name}_Mirrored") : obj;

        // Reflect world position along axis
        Vector3 pos = target.Transform.Position;
        float coord = GetAxisCoord(pos, axis);
        float mirroredCoord = 2.0f * worldPlaneCoordinate - coord;
        target.Transform.Position = SetAxisCoord(pos, axis, mirroredCoord);

        // Mirror rotation Euler degrees
        Vector3 euler = target.Transform.RotationEulerDegrees;
        Vector3 mirroredEuler = axis switch
        {
            Axis3D.X => new Vector3(euler.X, -euler.Y, -euler.Z),
            Axis3D.Y => new Vector3(-euler.X, euler.Y, -euler.Z),
            Axis3D.Z => new Vector3(-euler.X, -euler.Y, euler.Z),
            _ => euler
        };
        target.Transform.RotationEulerDegrees = mirroredEuler;

        // Mirror mesh locally across local origin
        if (target.Mesh is not null)
        {
            MirrorMesh(target.Mesh, axis, planeCoordinate: 0f, keepOriginal: false, weldThreshold);
        }

        return target;
    }

    /// <summary>
    /// Mirrors multiple SceneObjects across <paramref name="worldPlaneCoordinate"/> along <paramref name="axis"/>.
    /// </summary>
    public static IReadOnlyList<SceneObject> MirrorObjects(
        EditorScene scene,
        IReadOnlyCollection<int> objectIds,
        Axis3D axis,
        float worldPlaneCoordinate = 0f,
        bool keepOriginal = false,
        float weldThreshold = 0.001f)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(objectIds);

        var results = new List<SceneObject>(objectIds.Count);
        foreach (int id in objectIds)
        {
            SceneObject? mirrored = MirrorObject(scene, id, axis, worldPlaneCoordinate, keepOriginal, weldThreshold);
            if (mirrored is not null)
            {
                results.Add(mirrored);
            }
        }

        return results;
    }

    /// <summary>
    /// Clones the target mesh (or selected faces) <c>count - 1</c> additional times at <c>i * stepOffset</c>.
    /// </summary>
    public static void LinearArrayMesh(
        EditableMesh mesh,
        int count,
        Vector3 stepOffset,
        IReadOnlyCollection<int>? sourceFaceIds = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        if (count <= 1 || mesh.VertexCount == 0)
        {
            return;
        }

        if (sourceFaceIds is null)
        {
            var baseVertices = mesh.Vertices.ToList();
            var baseFaces = mesh.Faces.ToList();

            for (int step = 1; step < count; step++)
            {
                Vector3 offset = stepOffset * step;
                var oldToNew = new Dictionary<int, int>();

                for (int i = 0; i < baseVertices.Count; i++)
                {
                    MeshVertex v = baseVertices[i];
                    int newId = mesh.AddVertex(v.Position + offset);
                    oldToNew[v.Id] = newId;
                    if (mesh.GetVertex(newId) is { } newVert && (v.Metadata.Properties.Count > 0 || v.Metadata.Tags.Count > 0))
                    {
                        newVert.Metadata = v.Metadata.Clone();
                    }
                }

                for (int i = 0; i < baseFaces.Count; i++)
                {
                    PolygonFace f = baseFaces[i];
                    var newLoop = f.VertexIds.Select(id => oldToNew[id]).ToList();
                    int newFId = mesh.AddFace(newLoop, f.MaterialId);
                    if (mesh.GetFace(newFId) is { } newFace && (f.Metadata.Properties.Count > 0 || f.Metadata.Tags.Count > 0))
                    {
                        newFace.Metadata = f.Metadata.Clone();
                    }
                }

                var oldEdges = mesh.EdgeMetadata.ToList();
                foreach (var (edge, meta) in oldEdges)
                {
                    if (oldToNew.TryGetValue(edge.VMin, out int nMin) &&
                        oldToNew.TryGetValue(edge.VMax, out int nMax))
                    {
                        mesh.GetOrCreateEdgeMetadata(new EdgeKey(nMin, nMax)).MergeFrom(meta);
                    }
                }
            }

            mesh.ValidateAndRepair();
            return;
        }

        // Sub-element face array
        var targetFaces = new List<PolygonFace>();
        var uniqueVertexIds = new List<int>();
        var seenVertices = new HashSet<int>();

        foreach (int id in sourceFaceIds)
        {
            PolygonFace? f = mesh.GetFace(id);
            if (f is not null && f.VertexIds.Count >= 3)
            {
                targetFaces.Add(f);
                for (int i = 0; i < f.VertexIds.Count; i++)
                {
                    int vId = f.VertexIds[i];
                    if (seenVertices.Add(vId))
                    {
                        uniqueVertexIds.Add(vId);
                    }
                }
            }
        }

        if (targetFaces.Count == 0)
        {
            return;
        }

        for (int step = 1; step < count; step++)
        {
            Vector3 offset = stepOffset * step;
            var oldToNew = new Dictionary<int, int>();

            for (int i = 0; i < uniqueVertexIds.Count; i++)
            {
                int vId = uniqueVertexIds[i];
                MeshVertex? v = mesh.GetVertex(vId);
                if (v is not null)
                {
                    int newId = mesh.AddVertex(v.Position + offset);
                    oldToNew[vId] = newId;
                    if (mesh.GetVertex(newId) is { } newVert && (v.Metadata.Properties.Count > 0 || v.Metadata.Tags.Count > 0))
                    {
                        newVert.Metadata = v.Metadata.Clone();
                    }
                }
            }

            for (int i = 0; i < targetFaces.Count; i++)
            {
                PolygonFace f = targetFaces[i];
                var newLoop = f.VertexIds.Select(id => oldToNew[id]).ToList();
                int newFId = mesh.AddFace(newLoop, f.MaterialId);
                if (mesh.GetFace(newFId) is { } newFace && (f.Metadata.Properties.Count > 0 || f.Metadata.Tags.Count > 0))
                {
                    newFace.Metadata = f.Metadata.Clone();
                }
            }

            var oldEdges = mesh.EdgeMetadata.ToList();
            foreach (var (edge, meta) in oldEdges)
            {
                if (oldToNew.TryGetValue(edge.VMin, out int nMin) &&
                    oldToNew.TryGetValue(edge.VMax, out int nMax))
                {
                    mesh.GetOrCreateEdgeMetadata(new EdgeKey(nMin, nMax)).MergeFrom(meta);
                }
            }
        }

        mesh.ValidateAndRepair();
    }

    /// <summary>
    /// Clones the target objects <c>count - 1</c> additional times at <c>i * stepOffset</c>.
    /// Returns all newly created SceneObjects.
    /// </summary>
    public static IReadOnlyList<SceneObject> LinearArrayObjects(
        EditorScene scene,
        IReadOnlyCollection<int> objectIds,
        int count,
        Vector3 stepOffset)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(objectIds);

        if (count <= 1 || objectIds.Count == 0)
        {
            return [];
        }

        var targetObjects = new List<SceneObject>();
        foreach (int id in objectIds)
        {
            SceneObject? obj = scene.GetObject(id);
            if (obj is not null)
            {
                targetObjects.Add(obj);
            }
        }

        if (targetObjects.Count == 0)
        {
            return [];
        }

        var created = new List<SceneObject>((count - 1) * targetObjects.Count);
        for (int step = 1; step < count; step++)
        {
            Vector3 offset = stepOffset * step;
            for (int i = 0; i < targetObjects.Count; i++)
            {
                SceneObject obj = targetObjects[i];
                string name = $"{obj.Name}_Array{step}";
                SceneObject clone = scene.AddClonedObject(obj, name);
                clone.Transform.Position = obj.Transform.Position + offset;
                created.Add(clone);
            }
        }

        return created;
    }

    /// <summary>
    /// Repeats geometry <paramref name="count"/> times around <paramref name="pivot"/> and <paramref name="axis"/>
    /// with step angle <c>totalAngleDegrees / count</c>.
    /// </summary>
    public static void RadialArrayMesh(
        EditableMesh mesh,
        int count,
        Vector3 pivot,
        Axis3D axis,
        float totalAngleDegrees = 360f,
        IReadOnlyCollection<int>? sourceFaceIds = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        if (count <= 1 || mesh.VertexCount == 0)
        {
            return;
        }

        Vector3 axisVector = axis.ToVector3();
        float stepAngleDeg = totalAngleDegrees / count;
        float stepAngleRad = stepAngleDeg * (MathF.PI / 180.0f);

        if (sourceFaceIds is null)
        {
            var baseVertices = mesh.Vertices.ToList();
            var baseFaces = mesh.Faces.ToList();

            for (int step = 1; step < count; step++)
            {
                float angle = stepAngleRad * step;
                Quaternion q = Quaternion.CreateFromAxisAngle(axisVector, angle);
                var oldToNew = new Dictionary<int, int>();

                for (int i = 0; i < baseVertices.Count; i++)
                {
                    MeshVertex v = baseVertices[i];
                    Vector3 rel = v.Position - pivot;
                    Vector3 rotated = pivot + Vector3.Transform(rel, q);
                    int newId = mesh.AddVertex(rotated);
                    oldToNew[v.Id] = newId;
                    if (mesh.GetVertex(newId) is { } newVert && (v.Metadata.Properties.Count > 0 || v.Metadata.Tags.Count > 0))
                    {
                        newVert.Metadata = v.Metadata.Clone();
                    }
                }

                for (int i = 0; i < baseFaces.Count; i++)
                {
                    PolygonFace f = baseFaces[i];
                    var newLoop = f.VertexIds.Select(id => oldToNew[id]).ToList();
                    int newFId = mesh.AddFace(newLoop, f.MaterialId);
                    if (mesh.GetFace(newFId) is { } newFace && (f.Metadata.Properties.Count > 0 || f.Metadata.Tags.Count > 0))
                    {
                        newFace.Metadata = f.Metadata.Clone();
                    }
                }

                var oldEdges = mesh.EdgeMetadata.ToList();
                foreach (var (edge, meta) in oldEdges)
                {
                    if (oldToNew.TryGetValue(edge.VMin, out int nMin) &&
                        oldToNew.TryGetValue(edge.VMax, out int nMax))
                    {
                        mesh.GetOrCreateEdgeMetadata(new EdgeKey(nMin, nMax)).MergeFrom(meta);
                    }
                }
            }

            mesh.ValidateAndRepair();
            return;
        }

        // Sub-element faces
        var targetFaces = new List<PolygonFace>();
        var uniqueVertexIds = new List<int>();
        var seenVertices = new HashSet<int>();

        foreach (int id in sourceFaceIds)
        {
            PolygonFace? f = mesh.GetFace(id);
            if (f is not null && f.VertexIds.Count >= 3)
            {
                targetFaces.Add(f);
                for (int i = 0; i < f.VertexIds.Count; i++)
                {
                    int vId = f.VertexIds[i];
                    if (seenVertices.Add(vId))
                    {
                        uniqueVertexIds.Add(vId);
                    }
                }
            }
        }

        if (targetFaces.Count == 0)
        {
            return;
        }

        for (int step = 1; step < count; step++)
        {
            float angle = stepAngleRad * step;
            Quaternion q = Quaternion.CreateFromAxisAngle(axisVector, angle);
            var oldToNew = new Dictionary<int, int>();

            for (int i = 0; i < uniqueVertexIds.Count; i++)
            {
                int vId = uniqueVertexIds[i];
                MeshVertex? v = mesh.GetVertex(vId);
                if (v is not null)
                {
                    Vector3 rel = v.Position - pivot;
                    Vector3 rotated = pivot + Vector3.Transform(rel, q);
                    int newId = mesh.AddVertex(rotated);
                    oldToNew[vId] = newId;
                    if (mesh.GetVertex(newId) is { } newVert && (v.Metadata.Properties.Count > 0 || v.Metadata.Tags.Count > 0))
                    {
                        newVert.Metadata = v.Metadata.Clone();
                    }
                }
            }

            for (int i = 0; i < targetFaces.Count; i++)
            {
                PolygonFace f = targetFaces[i];
                var newLoop = f.VertexIds.Select(id => oldToNew[id]).ToList();
                int newFId = mesh.AddFace(newLoop, f.MaterialId);
                if (mesh.GetFace(newFId) is { } newFace && (f.Metadata.Properties.Count > 0 || f.Metadata.Tags.Count > 0))
                {
                    newFace.Metadata = f.Metadata.Clone();
                }
            }

            var oldEdges = mesh.EdgeMetadata.ToList();
            foreach (var (edge, meta) in oldEdges)
            {
                if (oldToNew.TryGetValue(edge.VMin, out int nMin) &&
                    oldToNew.TryGetValue(edge.VMax, out int nMax))
                {
                    mesh.GetOrCreateEdgeMetadata(new EdgeKey(nMin, nMax)).MergeFrom(meta);
                }
            }
        }

        mesh.ValidateAndRepair();
    }

    /// <summary>
    /// Repeats target objects <paramref name="count"/> times around <paramref name="worldPivot"/> and <paramref name="axis"/>
    /// with step angle <c>totalAngleDegrees / count</c>.
    /// Returns all newly created SceneObjects.
    /// </summary>
    public static IReadOnlyList<SceneObject> RadialArrayObjects(
        EditorScene scene,
        IReadOnlyCollection<int> objectIds,
        int count,
        Vector3 worldPivot,
        Axis3D axis,
        float totalAngleDegrees = 360f)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(objectIds);

        if (count <= 1 || objectIds.Count == 0)
        {
            return [];
        }

        var targetObjects = new List<SceneObject>();
        foreach (int id in objectIds)
        {
            SceneObject? obj = scene.GetObject(id);
            if (obj is not null)
            {
                targetObjects.Add(obj);
            }
        }

        if (targetObjects.Count == 0)
        {
            return [];
        }

        Vector3 axisVector = axis.ToVector3();
        float stepAngleDeg = totalAngleDegrees / count;
        float stepAngleRad = stepAngleDeg * (MathF.PI / 180.0f);

        var created = new List<SceneObject>((count - 1) * targetObjects.Count);
        for (int step = 1; step < count; step++)
        {
            float angleRad = stepAngleRad * step;
            Quaternion rotDelta = Quaternion.CreateFromAxisAngle(axisVector, angleRad);

            for (int i = 0; i < targetObjects.Count; i++)
            {
                SceneObject obj = targetObjects[i];
                string name = $"{obj.Name}_Radial{step}";
                SceneObject clone = scene.AddClonedObject(obj, name);

                Vector3 relPos = obj.Transform.Position - worldPivot;
                clone.Transform.Position = worldPivot + Vector3.Transform(relPos, rotDelta);

                Quaternion objRot = obj.Transform.GetRotationQuaternion();
                clone.Transform.SetRotationFromQuaternion(rotDelta * objRot);

                created.Add(clone);
            }
        }

        return created;
    }

    /// <summary>
    /// Joins multiple <see cref="SceneObject"/>s into a single mesh on the active (or first) target object,
    /// transforming source vertex positions into the target object's local coordinate space while preserving
    /// world-space UVs, materials, and element metadata. Removes the merged source objects from <paramref name="scene"/>.
    /// </summary>
    public static SceneObject? JoinObjects(
        EditorScene scene,
        IReadOnlyCollection<int> objectIds,
        SelectionManager? selection = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(objectIds);

        var validObjects = new List<SceneObject>();
        var seenIds = new HashSet<int>();
        foreach (int id in objectIds)
        {
            if (seenIds.Add(id) && scene.GetObject(id) is { Locked: false, Mesh: not null } obj)
            {
                validObjects.Add(obj);
            }
        }

        if (validObjects.Count < 2)
        {
            return null;
        }

        SceneObject targetObj = validObjects[0];
        if (selection?.ActiveObjectId is int activeId)
        {
            SceneObject? match = validObjects.FirstOrDefault(o => o.Id == activeId);
            if (match is not null)
            {
                targetObj = match;
            }
        }

        EditableMesh targetMesh = targetObj.Mesh!;
        Matrix4x4 targetInverse = targetObj.Transform.GetInverseLocalMatrix();

        for (int i = 0; i < validObjects.Count; i++)
        {
            SceneObject srcObj = validObjects[i];
            if (srcObj.Id == targetObj.Id)
            {
                continue;
            }

            EditableMesh srcMesh = srcObj.Mesh!;
            Matrix4x4 srcToWorld = srcObj.Transform.GetLocalMatrix();
            Matrix4x4 srcToTargetLocal = srcToWorld * targetInverse;
            bool flipWinding = srcToTargetLocal.GetDeterminant() < 0.0f;

            var oldToNewVert = new Dictionary<int, int>(srcMesh.Vertices.Count);
            for (int v = 0; v < srcMesh.Vertices.Count; v++)
            {
                MeshVertex srcVert = srcMesh.Vertices[v];
                Vector3 worldPos = Vector3.Transform(srcVert.Position, srcToWorld);
                Vector3 targetLocalPos = Vector3.Transform(worldPos, targetInverse);
                int newVId = targetMesh.AddVertex(targetLocalPos);
                oldToNewVert[srcVert.Id] = newVId;

                if (targetMesh.GetVertex(newVId) is { } dstVert &&
                    (srcVert.Metadata.Properties.Count > 0 || srcVert.Metadata.Tags.Count > 0))
                {
                    dstVert.Metadata = srcVert.Metadata.Clone();
                }
            }

            for (int f = 0; f < srcMesh.Faces.Count; f++)
            {
                PolygonFace srcFace = srcMesh.Faces[f];
                if (srcFace.VertexIds.Count < 3)
                {
                    continue;
                }

                var newLoop = new List<int>(srcFace.VertexIds.Count);
                for (int k = 0; k < srcFace.VertexIds.Count; k++)
                {
                    if (oldToNewVert.TryGetValue(srcFace.VertexIds[k], out int mappedVId))
                    {
                        newLoop.Add(mappedVId);
                    }
                }

                if (newLoop.Count < 3)
                {
                    continue;
                }

                if (flipWinding)
                {
                    newLoop.Reverse();
                }

                FaceUvMapping clonedUv = srcFace.UvMapping.Clone();
                int newFId = targetMesh.AddFace(newLoop, srcFace.MaterialId, clonedUv);
                if (targetMesh.GetFace(newFId) is { } dstFace)
                {
                    if (srcFace.Metadata.Properties.Count > 0 || srcFace.Metadata.Tags.Count > 0)
                    {
                        dstFace.Metadata = srcFace.Metadata.Clone();
                    }

                    if (!dstFace.UvMapping.TextureLock)
                    {
                        UvProjectionEngine.RefreshUnlockedFaceAxes(dstFace, targetMesh, targetObj.Transform);
                    }
                }
            }

            foreach (var (edge, meta) in srcMesh.EdgeMetadata)
            {
                if (oldToNewVert.TryGetValue(edge.VMin, out int nMin) &&
                    oldToNewVert.TryGetValue(edge.VMax, out int nMax) &&
                    nMin != nMax)
                {
                    targetMesh.GetOrCreateEdgeMetadata(new EdgeKey(nMin, nMax)).MergeFrom(meta);
                }
            }

            scene.RemoveObject(srcObj.Id);
        }

        targetMesh.ValidateAndRepair();
        selection?.SelectSingleObject(targetObj.Id);
        return targetObj;
    }

    /// <summary>
    /// Extracts the specified <paramref name="faceIds"/> from <paramref name="sourceObjectId"/> into a newly
    /// created <see cref="SceneObject"/> with an identical world transform, removing the separated faces from
    /// the source mesh and running <see cref="EditableMesh.ValidateAndRepair"/> on both meshes.
    /// </summary>
    public static SceneObject? SeparateFaces(
        EditorScene scene,
        int sourceObjectId,
        IReadOnlyCollection<int> faceIds,
        SelectionManager? selection = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(faceIds);

        if (faceIds.Count == 0)
        {
            return null;
        }

        SceneObject? sourceObj = scene.GetObject(sourceObjectId);
        if (sourceObj?.Mesh is null || sourceObj.Locked)
        {
            return null;
        }

        EditableMesh sourceMesh = sourceObj.Mesh;
        var targetFaces = new List<PolygonFace>();
        var seenFaces = new HashSet<int>();

        foreach (int fid in faceIds)
        {
            if (seenFaces.Add(fid) && sourceMesh.GetFace(fid) is { } face && face.VertexIds.Count >= 3)
            {
                targetFaces.Add(face);
            }
        }

        if (targetFaces.Count == 0 || targetFaces.Count >= sourceMesh.FaceCount)
        {
            return null;
        }

        var separatedMesh = new EditableMesh();
        var oldToNewVert = new Dictionary<int, int>();

        for (int i = 0; i < targetFaces.Count; i++)
        {
            PolygonFace face = targetFaces[i];
            var newLoop = new List<int>(face.VertexIds.Count);

            for (int k = 0; k < face.VertexIds.Count; k++)
            {
                int oldVId = face.VertexIds[k];
                if (!oldToNewVert.TryGetValue(oldVId, out int newVId))
                {
                    MeshVertex? srcVert = sourceMesh.GetVertex(oldVId);
                    if (srcVert is null)
                    {
                        continue;
                    }

                    newVId = separatedMesh.AddVertex(srcVert.Position);
                    oldToNewVert[oldVId] = newVId;

                    if (separatedMesh.GetVertex(newVId) is { } dstVert &&
                        (srcVert.Metadata.Properties.Count > 0 || srcVert.Metadata.Tags.Count > 0))
                    {
                        dstVert.Metadata = srcVert.Metadata.Clone();
                    }
                }

                newLoop.Add(newVId);
            }

            if (newLoop.Count >= 3)
            {
                int newFId = separatedMesh.AddFace(newLoop, face.MaterialId, face.UvMapping.Clone());
                if (separatedMesh.GetFace(newFId) is { } dstFace &&
                    (face.Metadata.Properties.Count > 0 || face.Metadata.Tags.Count > 0))
                {
                    dstFace.Metadata = face.Metadata.Clone();
                }
            }
        }

        foreach (var (edge, meta) in sourceMesh.EdgeMetadata)
        {
            if (oldToNewVert.TryGetValue(edge.VMin, out int nMin) &&
                oldToNewVert.TryGetValue(edge.VMax, out int nMax) &&
                nMin != nMax)
            {
                separatedMesh.GetOrCreateEdgeMetadata(new EdgeKey(nMin, nMax)).MergeFrom(meta);
            }
        }

        for (int i = 0; i < targetFaces.Count; i++)
        {
            sourceMesh.RemoveFace(targetFaces[i].Id);
        }

        sourceMesh.ValidateAndRepair();
        separatedMesh.ValidateAndRepair();

        SceneObject newObj = scene.AddMeshObject($"{sourceObj.Name}_Part", separatedMesh, sourceObj.Transform.Position);
        newObj.Transform.RotationEulerDegrees = sourceObj.Transform.RotationEulerDegrees;
        newObj.Transform.Scale = sourceObj.Transform.Scale;
        if (sourceObj.Metadata.Properties.Count > 0 || sourceObj.Metadata.Tags.Count > 0)
        {
            newObj.Metadata = sourceObj.Metadata.Clone();
        }

        selection?.SanitizeAgainstScene(scene);
        return newObj;
    }

    /// <summary>
    /// Bakes selected components of <paramref name="obj"/>'s transform into its mesh vertex positions
    /// while preserving world-space vertex positions and UVs.
    /// </summary>
    public static bool ApplyTransform(
        SceneObject obj,
        bool applyPosition = false,
        bool applyRotation = true,
        bool applyScale = true)
        => ObjectTransformOps.ApplyTransform(obj, applyPosition, applyRotation, applyScale);

    /// <summary>
    /// Moves <paramref name="obj"/>'s origin/pivot to the center of its local bounding box without moving geometry in world space.
    /// </summary>
    public static bool SetOriginToBoundsCenter(SceneObject obj)
        => ObjectTransformOps.SetOriginToBoundsCenter(obj);

    /// <summary>
    /// Moves <paramref name="obj"/>'s origin/pivot to the bottom center (<c>Min.Y</c>) of its local bounding box without moving geometry in world space.
    /// </summary>
    public static bool SetOriginToBottomCenter(SceneObject obj)
        => ObjectTransformOps.SetOriginToBottomCenter(obj);

    private static float GetAxisCoord(Vector3 v, Axis3D axis) => axis switch
    {
        Axis3D.X => v.X,
        Axis3D.Y => v.Y,
        Axis3D.Z => v.Z,
        _ => v.X
    };

    private static Vector3 SetAxisCoord(Vector3 v, Axis3D axis, float value) => axis switch
    {
        Axis3D.X => new Vector3(value, v.Y, v.Z),
        Axis3D.Y => new Vector3(v.X, value, v.Z),
        Axis3D.Z => new Vector3(v.X, v.Y, value),
        _ => v
    };
}
