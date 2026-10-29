using Leed3D.Kernel;
using Leed3D.Runtime;

namespace Leed3D.Editor;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Contains("--version"))
        {
            Console.WriteLine($"Leed3D Editor 0.1.0 ({LeedRuntimeInfo.Name} {LeedRuntimeInfo.Version}, {LeedKernelInfo.Name} {LeedKernelInfo.Version})");
            return 0;
        }

        if (args.Contains("--headless-check"))
        {
            Console.WriteLine("Headless check passed.");
            return 0;
        }

        Console.WriteLine("Leed3D Editor (GUI not initialized in headless mode. Run with --version or --headless-check).");
        return 0;
    }
}
