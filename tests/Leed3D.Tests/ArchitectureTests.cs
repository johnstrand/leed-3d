using System.Reflection;
using Leed3D.Kernel;
using Leed3D.Runtime;

namespace Leed3D.Tests;

public class ArchitectureTests
{
    private static readonly string[] ForbiddenUiAssemblies = ["Raylib-cs", "ImGui.NET"];

    [Fact]
    public void Runtime_DoesNotReference_RaylibOrImGui()
    {
        Assembly runtimeAssembly = typeof(LeedRuntimeInfo).Assembly;
        AssemblyName[] referencedAssemblies = runtimeAssembly.GetReferencedAssemblies();

        foreach (var forbidden in ForbiddenUiAssemblies)
        {
            Assert.DoesNotContain(referencedAssemblies, a => string.Equals(a.Name, forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Runtime_HasNoExternalNuGetDependencies()
    {
        Assembly runtimeAssembly = typeof(LeedRuntimeInfo).Assembly;
        AssemblyName[] referencedAssemblies = runtimeAssembly.GetReferencedAssemblies();

        foreach (var assemblyRef in referencedAssemblies)
        {
            string name = assemblyRef.Name ?? string.Empty;
            Assert.True(
                name == "System.Runtime" ||
                name.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) ||
                name == "netstandard" ||
                name == "mscorlib",
                $"Leed3D.Runtime should only reference system assemblies, but references: {name}");
        }
    }

    [Fact]
    public void Kernel_DoesNotReference_RaylibOrImGui()
    {
        Assembly kernelAssembly = typeof(LeedKernelInfo).Assembly;
        AssemblyName[] referencedAssemblies = kernelAssembly.GetReferencedAssemblies();

        foreach (var forbidden in ForbiddenUiAssemblies)
        {
            Assert.DoesNotContain(referencedAssemblies, a => string.Equals(a.Name, forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Kernel_ReferencesRuntime()
    {
        Assembly kernelAssembly = typeof(LeedKernelInfo).Assembly;
        AssemblyName[] referencedAssemblies = kernelAssembly.GetReferencedAssemblies();

        Assert.Contains(referencedAssemblies, a => string.Equals(a.Name, "Leed3D.Runtime", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MetadataInfo_ReturnsExpectedDefaults()
    {
        Assert.Equal("Leed3D.Runtime", LeedRuntimeInfo.Name);
        Assert.Equal("Leed3D.Kernel", LeedKernelInfo.Name);
        Assert.False(string.IsNullOrWhiteSpace(LeedRuntimeInfo.Version));
        Assert.False(string.IsNullOrWhiteSpace(LeedKernelInfo.Version));
    }
}
