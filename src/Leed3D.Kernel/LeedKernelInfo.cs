using Leed3D.Runtime;

namespace Leed3D.Kernel;

public static class LeedKernelInfo
{
    public const string Name = "Leed3D.Kernel";
    public const string Version = "0.1.0";

    // Returns a type from Leed3D.Runtime to force TypeDef/TypeRef emitted into assembly metadata
    public static Type RuntimeType => typeof(LeedRuntimeInfo);
}
