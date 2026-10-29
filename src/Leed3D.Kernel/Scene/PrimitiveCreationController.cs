using System.Numerics;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Primitives;

namespace Leed3D.Kernel.Scene;

public enum PrimitiveType
{
    Plane,
    Box,
    Cylinder,
    Cone,
    Icosahedron,
    Geosphere
}

/// <summary>
/// Headless controller managing parametric primitive spawning and live parameter adjustments
/// (e.g. dimensions, segment counts, sphere subdivisions, hollow room inversion)
/// while maintaining a single Undo entry for the entire creation step.
/// </summary>
public sealed class PrimitiveCreationController
{
    private readonly EditorScene _scene;
    private readonly UndoRedoStack _undoStack;

    public SceneObject? ActiveObject { get; private set; }
    public PrimitiveType? ActivePrimitiveType { get; private set; }
    public bool IsActive => ActiveObject is not null && ActivePrimitiveType.HasValue;

    // Parameter models
    public PlaneParams PlaneParams { get; set; } = new(4.0f, 4.0f, 1, 1);
    public BoxParams BoxParams { get; set; } = BoxParams.Default;
    public CylinderParams CylinderParams { get; set; } = new(1.0f, 2.0f, 16, 1, true, true);
    public ConeParams ConeParams { get; set; } = new(1.0f, 2.0f, 16, true);
    public IcosahedronParams IcosahedronParams { get; set; } = new(1.0f);
    public GeosphereParams GeosphereParams { get; set; } = new(1.0f, 2);

    public PrimitiveCreationController(EditorScene scene, UndoRedoStack undoStack)
    {
        _scene = scene ?? throw new ArgumentNullException(nameof(scene));
        _undoStack = undoStack ?? throw new ArgumentNullException(nameof(undoStack));
    }

    /// <summary>
    /// Spawns a primitive of the specified type at the given spawn position,
    /// recording an undo record for the spawn operation and making it active for adjustments.
    /// </summary>
    public SceneObject SpawnPrimitive(PrimitiveType type, Vector3? spawnPosition = null)
    {
        EditorScene beforeState = _scene.CloneSnapshot();
        Vector3 position = spawnPosition ?? Vector3.Zero;

        ActivePrimitiveType = type;

        EditableMesh mesh;
        string objectName;

        switch (type)
        {
            case PrimitiveType.Plane:
                PlaneParams = new PlaneParams(4.0f, 4.0f, 1, 1);
                mesh = PrimitiveGenerator.CreatePlane(PlaneParams);
                objectName = "Plane";
                break;

            case PrimitiveType.Box:
                BoxParams = BoxParams.Default;
                mesh = PrimitiveGenerator.CreateBox(BoxParams);
                objectName = "Box";
                break;

            case PrimitiveType.Cylinder:
                CylinderParams = new CylinderParams(1.0f, 2.0f, 16, 1, true, true);
                mesh = PrimitiveGenerator.CreateCylinder(CylinderParams);
                objectName = "Cylinder";
                break;

            case PrimitiveType.Cone:
                ConeParams = new ConeParams(1.0f, 2.0f, 16, true);
                mesh = PrimitiveGenerator.CreateCone(ConeParams);
                objectName = "Cone";
                break;

            case PrimitiveType.Icosahedron:
                IcosahedronParams = new IcosahedronParams(1.0f);
                mesh = PrimitiveGenerator.CreateIcosahedron(IcosahedronParams);
                objectName = "Icosahedron";
                break;

            case PrimitiveType.Geosphere:
                GeosphereParams = new GeosphereParams(1.0f, 2);
                mesh = PrimitiveGenerator.CreateGeosphere(GeosphereParams);
                objectName = "Geosphere";
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }

        ActiveObject = _scene.AddMeshObject(objectName, mesh, position);
        _undoStack.RecordAction($"Add {objectName}", beforeState, _scene);

        return ActiveObject;
    }

    /// <summary>
    /// Re-evaluates and replaces the mesh of the active primitive using its current parameter state.
    /// Does not push extra undo entries, preserving the single undo snapshot from SpawnPrimitive.
    /// </summary>
    public void RegenerateActiveMesh()
    {
        if (ActiveObject is null || !ActivePrimitiveType.HasValue)
        {
            return;
        }

        // Verify object still exists in scene (e.g. wasn't undone or deleted)
        if (_scene.GetObject(ActiveObject.Id) is null)
        {
            Dismiss();
            return;
        }

        EditableMesh newMesh = ActivePrimitiveType.Value switch
        {
            PrimitiveType.Plane => PrimitiveGenerator.CreatePlane(PlaneParams),
            PrimitiveType.Box => PrimitiveGenerator.CreateBox(BoxParams),
            PrimitiveType.Cylinder => PrimitiveGenerator.CreateCylinder(CylinderParams),
            PrimitiveType.Cone => PrimitiveGenerator.CreateCone(ConeParams),
            PrimitiveType.Icosahedron => PrimitiveGenerator.CreateIcosahedron(IcosahedronParams),
            PrimitiveType.Geosphere => PrimitiveGenerator.CreateGeosphere(GeosphereParams),
            _ => throw new ArgumentOutOfRangeException()
        };

        ActiveObject.Mesh = newMesh;
    }

    /// <summary>
    /// Dismisses the active primitive adjustment overlay so subsequent edits do not modify it.
    /// </summary>
    public void Dismiss()
    {
        ActiveObject = null;
        ActivePrimitiveType = null;
    }
}
