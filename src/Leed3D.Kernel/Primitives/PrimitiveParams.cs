using System.Numerics;

namespace Leed3D.Kernel.Primitives;

public readonly record struct PlaneParams(
    float Width = 4.0f,
    float Depth = 4.0f,
    int WidthSegments = 1,
    int DepthSegments = 1,
    string MaterialId = "dev_grid_gray");

public readonly record struct BoxParams(
    Vector3 Size,
    int SegmentsX = 1,
    int SegmentsY = 1,
    int SegmentsZ = 1,
    bool InvertNormals = false,
    string MaterialId = "dev_grid_gray")
{
    public static BoxParams Default => new(new Vector3(2.0f, 2.0f, 2.0f));
}

public readonly record struct CylinderParams(
    float Radius = 1.0f,
    float Height = 2.0f,
    int RadialSegments = 16,
    int HeightSegments = 1,
    bool CapTop = true,
    bool CapBottom = true,
    string MaterialId = "dev_grid_gray");

public readonly record struct ConeParams(
    float Radius = 1.0f,
    float Height = 2.0f,
    int RadialSegments = 16,
    bool CapBottom = true,
    string MaterialId = "dev_grid_gray");

public readonly record struct IcosahedronParams(
    float Radius = 1.0f,
    string MaterialId = "dev_grid_gray");

public readonly record struct GeosphereParams(
    float Radius = 1.0f,
    int Subdivisions = 2,
    string MaterialId = "dev_grid_gray");
