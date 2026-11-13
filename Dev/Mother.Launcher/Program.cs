// @module: Mother.Launcher.Program
// @purpose: Clean-root Mother.exe shim delegating CLI/GUI forwarding to embedded Foundation.Launcher.dll.
// @depends-on: System, System.IO, System.Reflection, System.Runtime.CompilerServices, Foundation.Launcher
// @exports: Program

using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Foundation.Launcher;

namespace Mother.Launcher;

internal static class Program
{
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            if (new AssemblyName(e.Name).Name == "Foundation.Launcher")
            {
                using Stream? stream = typeof(Program).Assembly.GetManifestResourceStream("Foundation.Launcher.dll");
                if (stream is not null)
                {
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    return Assembly.Load(ms.ToArray());
                }
            }
            return null;
        };
        return InvokeRootHost(args);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int InvokeRootHost(string[] args) =>
        RootLauncherHost.Run(Path.Combine("app", "Mother.App.exe"), "Mother", args);
}
