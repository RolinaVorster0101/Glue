using System;
using System.Diagnostics;

namespace Glue.Services;

/// <summary>
/// A basic embedded terminal — spawns a persistent PowerShell process with
/// redirected stdin/stdout/stderr, letting you type commands and see their
/// output live.
///
/// This is deliberately NOT a full terminal emulator: no ANSI colors, no
/// cursor repositioning, no full-screen interactive apps (vim, htop, etc.)
/// — just plain text in, plain text out. That covers the common IDE case
/// (running `dotnet`, `git`, `npm`, `dir`, etc. and seeing what happened)
/// without pretending to be something much bigger (real terminal emulation
/// is a genuinely deep, separate undertaking).
/// </summary>
public class TerminalService : IDisposable
{
    private Process? _process;

    public event Action<string>? OutputReceived;

    public void Start()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            // No -Command/-File — PowerShell reads successive lines from
            // redirected stdin as commands, similar to a REPL, until the
            // process exits or "exit" is typed.
            Arguments = "-NoLogo -NoProfile",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Same MSBuildLocator environment-variable conflict as BuildService/
        // RunService — if you run `dotnet build`/`dotnet run` from inside
        // this terminal, it would inherit the same broken env vars otherwise.
        foreach (var key in new[] { "MSBUILD_EXE_PATH", "MSBuildExtensionsPath", "MSBuildSDKsPath" })
        {
            psi.Environment.Remove(key);
        }

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) OutputReceived?.Invoke(e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) OutputReceived?.Invoke(e.Data); };

        _process.Start();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public void SendCommand(string command)
    {
        if (_process is null || _process.HasExited) return;
        _process.StandardInput.WriteLine(command);
        _process.StandardInput.Flush();
    }

    public void Dispose()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Already exited between the check and the kill — fine.
        }

        _process?.Dispose();
    }
}
