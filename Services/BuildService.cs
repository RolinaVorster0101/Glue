using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace Glue.Services;

public record BuildResult(bool Success, string Output);

/// <summary>
/// Runs `dotnet build` against a .csproj as a real subprocess, capturing
/// stdout/stderr. This is genuinely running the actual .NET build toolchain —
/// not Roslyn's in-process compile used for live diagnostics (Services/
/// RoslynDiagnosticsService.cs) — so it reflects true build behavior (NuGet
/// restore failures, MSBuild target errors, etc.) the same way running
/// `dotnet build` in a terminal would.
/// </summary>
public static class BuildService
{
    public static async Task<BuildResult> BuildAsync(string csprojPath, Action<string>? onOutputLine = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{csprojPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // MSBuildLocator.RegisterDefaults() (see Program.cs) sets MSBuild-
        // related environment variables on OUR process so Roslyn's
        // MSBuildWorkspace can find the right toolset in-process. Those
        // variables get inherited by this child `dotnet build` process by
        // default, which can point the plain dotnet CLI at mismatched/
        // internal MSBuild paths — confirmed cause of a real MSB4018
        // "Method not found: IsSupportedOS()" error. Stripping them so the
        // child process resolves its own MSBuild normally, the same way
        // running `dotnet build` from a fresh terminal would.
        foreach (var key in new[] { "MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBuildSDKsPath" })
        {
            psi.Environment.Remove(key);
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var outputBuilder = new StringBuilder();

        void HandleLine(string? line)
        {
            if (line is null) return;
            outputBuilder.AppendLine(line);
            onOutputLine?.Invoke(line);
        }

        process.OutputDataReceived += (_, e) => HandleLine(e.Data);
        process.ErrorDataReceived += (_, e) => HandleLine(e.Data);

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        return new BuildResult(process.ExitCode == 0, outputBuilder.ToString());
    }
}
