// @module: Mother.Server.Program
// @purpose: Standalone Kestrel gateway entry point for headless daemon hosting and WebApplicationFactory test discovery.
// @depends-on: System, System.Threading.Tasks, Mother.Core
// @exports: Program

using System;
using System.Threading.Tasks;
using Mother.Core;

namespace Mother.Server;

public partial class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var runtime = new MotherRuntime(args, skipIpcMutex: false);
        if (runtime.Paths.Options.Headless)
        {
            runtime.Diagnostics.Log("INFO", "mother", "Server", "Headless server self-check succeeded.");
            return 0;
        }

        runtime.StartBackgroundSupervision();
        var app = MotherWebHost.BuildWebApplication(runtime, args);
        await app.RunAsync();
        return 0;
    }
}
