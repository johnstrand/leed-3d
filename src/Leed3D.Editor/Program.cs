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
            using var headlessCheckApp = new EditorApp();
            Console.WriteLine($"Headless check passed. Starter scene objects: {headlessCheckApp.Scene.ObjectCount}");
            return 0;
        }

        using var app = new EditorApp();
        app.Run();
        return 0;
    }
}
