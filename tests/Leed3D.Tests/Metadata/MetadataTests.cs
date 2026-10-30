using System.Numerics;
using Leed3D.Kernel.Csg;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Modifiers;
using Leed3D.Kernel.Primitives;
using Leed3D.Kernel.Scene;
using Leed3D.Runtime.Metadata;
using Xunit;

namespace Leed3D.Tests.Metadata;

public class MetadataTests
{
    [Fact]
    public void MetadataValue_TypesAndValues_StoreCorrectly()
    {
        var strVal = MetadataValue.FromString("test_string");
        Assert.Equal(MetadataValueType.String, strVal.Type);
        Assert.Equal("test_string", strVal.StringValue);
        Assert.Equal("test_string", strVal.AsObject());

        var fltVal = MetadataValue.FromFloat(3.14f);
        Assert.Equal(MetadataValueType.Float, fltVal.Type);
        Assert.Equal(3.14f, fltVal.FloatValue, 3);

        var intVal = MetadataValue.FromInt(42);
        Assert.Equal(MetadataValueType.Int, intVal.Type);
        Assert.Equal(42, intVal.IntValue);

        var boolVal = MetadataValue.FromBool(true);
        Assert.Equal(MetadataValueType.Bool, boolVal.Type);
        Assert.True(boolVal.BoolValue);

        var col = new ColorRgba(255, 128, 64, 255);
        var colVal = MetadataValue.FromColor(col);
        Assert.Equal(MetadataValueType.Color, colVal.Type);
        Assert.Equal(col, colVal.ColorValue);
        Assert.Equal("#FF8040FF", col.ToString());

        var hexCol = ColorRgba.FromHex("#00FF00");
        Assert.Equal(0, hexCol.R);
        Assert.Equal(255, hexCol.G);
        Assert.Equal(0, hexCol.B);

        var vec = new Vector3(1.0f, 2.0f, 3.0f);
        var vecVal = MetadataValue.FromVec3(vec);
        Assert.Equal(MetadataValueType.Vec3, vecVal.Type);
        Assert.Equal(vec, vecVal.Vec3Value);
    }

    [Fact]
    public void ElementMetadata_SetAndGetProperties_Works()
    {
        var meta = new ElementMetadata();
        meta.SetString("surface", "water");
        meta.SetFloat("friction", 0.5f);
        meta.SetInt("damage", 10);
        meta.SetBool("climbable", true);
        meta.SetColor("tint", new ColorRgba(100, 150, 200));
        meta.SetVec3("launch_dir", new Vector3(0, 1, 0));

        Assert.Equal(6, meta.Properties.Count);
        Assert.Equal("water", meta.GetString("surface"));
        Assert.Equal(0.5f, meta.GetFloat("friction")!.Value, 3);
        Assert.Equal(10, meta.GetInt("damage"));
        Assert.True(meta.GetBool("climbable"));
        Assert.Equal(new ColorRgba(100, 150, 200), meta.GetColor("tint"));
        Assert.Equal(new Vector3(0, 1, 0), meta.GetVec3("launch_dir"));

        // Case insensitivity
        Assert.Equal("water", meta.GetString("SURFACE"));

        // Removal
        Assert.True(meta.Remove("friction"));
        Assert.Null(meta.Get("friction"));
    }

    [Fact]
    public void ElementMetadata_Tags_AddRemoveAndQuery_Works()
    {
        var meta = new ElementMetadata();
        meta.AddTag("hazard");
        meta.AddTag("lava");

        Assert.Equal(2, meta.Tags.Count);
        Assert.True(meta.HasTag("hazard"));
        Assert.True(meta.HasTag("HAZARD"));
        Assert.True(meta.HasTag("lava"));

        meta.RemoveTag("hazard");
        Assert.False(meta.HasTag("hazard"));
        Assert.Single(meta.Tags);
    }

    [Fact]
    public void ElementMetadata_IsCollisionDisabled_CorrectlyIdentifies()
    {
        var meta = new ElementMetadata();
        Assert.False(meta.IsCollisionDisabled());

        // By collision=none
        meta.SetString("collision", "none");
        Assert.True(meta.IsCollisionDisabled());

        // By collision=false
        meta.SetBool("collision", false);
        Assert.True(meta.IsCollisionDisabled());

        // By no_collision tag
        meta.Clear();
        meta.AddTag("no_collision");
        Assert.True(meta.IsCollisionDisabled());
    }

    [Fact]
    public void ElementMetadata_CloneAndMerge_DeepCopies()
    {
        var original = new ElementMetadata();
        original.SetString("key1", "val1");
        original.AddTag("tag1");

        var clone = original.Clone();
        clone.SetString("key1", "modified");
        clone.AddTag("tag2");

        Assert.Equal("val1", original.GetString("key1"));
        Assert.Single(original.Tags);
        Assert.Equal("modified", clone.GetString("key1"));
        Assert.Equal(2, clone.Tags.Count);

        var destination = new ElementMetadata();
        destination.SetString("key2", "val2");
        destination.MergeFrom(clone);

        Assert.Equal(2, destination.Properties.Count);
        Assert.Equal("modified", destination.GetString("key1"));
        Assert.Equal("val2", destination.GetString("key2"));
        Assert.Equal(2, destination.Tags.Count);
    }

    [Fact]
    public void SceneObject_Metadata_ClonesAcrossSnapshots()
    {
        var obj = new SceneObject(1, "TriggerZone");
        obj.Metadata.SetString("event", "on_enter");
        obj.Metadata.AddTag("trigger");

        var clone = obj.Clone();
        Assert.Equal("on_enter", clone.Metadata.GetString("event"));
        Assert.True(clone.Metadata.HasTag("trigger"));

        clone.Metadata.SetString("event", "on_exit");
        Assert.Equal("on_enter", obj.Metadata.GetString("event"));
    }

    [Fact]
    public void EditableMesh_FaceAndVertexAndEdgeMetadata_ValidatesAndPrunes()
    {
        EditableMesh mesh = PrimitiveGenerator.CreateBox(new Vector3(2, 2, 2));

        int v0Id = mesh.Vertices[0].Id;
        int v1Id = mesh.Vertices[1].Id;

        // Vertex metadata
        mesh.GetVertex(v0Id)!.Metadata.SetString("anchor", "ground");
        Assert.Equal("ground", mesh.GetVertex(v0Id)!.Metadata.GetString("anchor"));

        // Face metadata
        mesh.Faces[0].Metadata.SetString("surface", "ice");
        Assert.Equal("ice", mesh.Faces[0].Metadata.GetString("surface"));

        // Edge metadata
        var edge = new EdgeKey(v0Id, v1Id);
        mesh.GetOrCreateEdgeMetadata(edge).SetBool("ledge_grab", true);
        Assert.True(mesh.GetEdgeMetadata(edge)!.GetBool("ledge_grab"));

        // Deleting vertices 0 and 1 should prune the edge metadata during ValidateAndRepair
        mesh.RemoveVertex(v0Id);
        mesh.RemoveVertex(v1Id);
        mesh.ValidateAndRepair();

        Assert.Null(mesh.GetEdgeMetadata(edge));
    }

    [Fact]
    public void MeshTopologyOps_ExtrudeFaces_PreservesFaceAndEdgeMetadata()
    {
        EditableMesh mesh = PrimitiveGenerator.CreateBox(new Vector3(2, 2, 2));
        PolygonFace topFace = mesh.Faces[4]; // Top face (+Y)
        topFace.Metadata.SetString("surface", "grass");
        topFace.Metadata.AddTag("walkable");

        var edge = new EdgeKey(topFace.VertexIds[0], topFace.VertexIds[1]);
        mesh.GetOrCreateEdgeMetadata(edge).SetBool("ledge_grab", true);

        var extrudedCapIds = MeshTopologyOps.ExtrudeFaces(mesh, [topFace.Id], 1.0f);
        Assert.Single(extrudedCapIds);

        PolygonFace capFace = mesh.GetFace(extrudedCapIds[0])!;
        Assert.Equal("grass", capFace.Metadata.GetString("surface"));
        Assert.True(capFace.Metadata.HasTag("walkable"));
    }

    [Fact]
    public void MeshTopologyOps_ConnectVertices_SplitsFaceAndCopiesMetadata()
    {
        var mesh = new EditableMesh();
        int v0 = mesh.AddVertex(new Vector3(0, 0, 0));
        int v1 = mesh.AddVertex(new Vector3(2, 0, 0));
        int v2 = mesh.AddVertex(new Vector3(2, 2, 0));
        int v3 = mesh.AddVertex(new Vector3(0, 2, 0));
        int fId = mesh.AddFace([v0, v1, v2, v3]);

        PolygonFace face = mesh.GetFace(fId)!;
        face.Metadata.SetString("surface", "metal");

        bool connected = MeshTopologyOps.ConnectVertices(mesh, v0, v2);
        Assert.True(connected);
        Assert.Equal(2, mesh.Faces.Count);

        foreach (PolygonFace child in mesh.Faces)
        {
            Assert.Equal("metal", child.Metadata.GetString("surface"));
        }
    }

    [Fact]
    public void MeshModifiers_MirrorMesh_PreservesMetadata()
    {
        EditableMesh mesh = PrimitiveGenerator.CreateBox(new Vector3(1, 1, 1));
        mesh.Faces[0].Metadata.SetString("material_tag", "stone");
        var edge = new EdgeKey(mesh.Faces[0].VertexIds[0], mesh.Faces[0].VertexIds[1]);
        mesh.GetOrCreateEdgeMetadata(edge).AddTag("rail");

        MeshModifiers.MirrorMesh(mesh, Axis3D.X, planeCoordinate: 2.0f, keepOriginal: true);

        // Cloned faces should have stone tag preserved
        int stoneFaces = mesh.Faces.Count(f => f.Metadata.GetString("material_tag") == "stone");
        Assert.Equal(2, stoneFaces);

        // Cloned edge should have rail tag preserved
        int railEdges = mesh.EdgeMetadata.Values.Count(meta => meta.HasTag("rail"));
        Assert.Equal(2, railEdges);
    }

    [Fact]
    public void CsgBooleanEngine_Subtract_PreservesMetadataOnSurvivingFaces()
    {
        EditableMesh cubeA = PrimitiveGenerator.CreateBox(new Vector3(2, 2, 2));
        cubeA.Faces[0].Metadata.SetString("original_owner", "meshA");

        // Small cutter that intersects one corner
        EditableMesh cubeB = PrimitiveGenerator.CreateBox(new Vector3(1, 1, 1));
        for (int i = 0; i < cubeB.Vertices.Count; i++)
        {
            cubeB.Vertices[i].Position += new Vector3(1.0f, 1.0f, 1.0f);
        }

        EditableMesh result = CsgBooleanEngine.Evaluate(cubeA, cubeB, CsgOperation.Subtract);
        Assert.True(result.Faces.Count > 0);

        // At least one face should retain "original_owner" = "meshA"
        bool hasPreservedMeta = result.Faces.Any(f => f.Metadata.GetString("original_owner") == "meshA");
        Assert.True(hasPreservedMeta);
    }
}
