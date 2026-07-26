using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Glue.Services;

public record RunResult(int ExitCode);

/// <summary>
/// Runs `dotnet run` against a .csproj as a real subprocess — this rebuilds
/// if needed, then launches the compiled app, streaming its console output
/// live. Basic Run integration (Phase 2 item 8); no debugger attached yet —
/// that's DAP integration in Phase 4.
/// </summary>
public static class RunService
{
    public static async Task<RunResult> RunAsync(
        string csprojPath, Action<string> onOutputLine, CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{csprojPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Same MSBuildLocator environment-variable conflict as BuildService —
        // see that file's comment for the full explanation.
        foreach (var key in new[] { "MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBuildSDKsPath" })
        {
            psi.Environment.Remove(key);
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        void HandleLine(string? line)
        {
            if (line is not null) onOutputLine(line);
        }

        process.OutputDataReceived += (_, e) => HandleLine(e.Data);
        process.ErrorDataReceived += (_, e) => HandleLine(e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using (cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Already exited between the check and the kill — fine, nothing to do.
            }
        }))
        {
            await process.WaitForExitAsync(cancellationToken);
        }

        return new RunResult(process.ExitCode);
    }
}
