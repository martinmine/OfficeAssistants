using System.Diagnostics;

namespace OfficeAssistant.Mac.Platform;

/// <summary>
/// Apps started from Finder or the Dock get a minimal PATH (/usr/bin:/bin:/usr/sbin:/sbin),
/// so MCP servers started with npx or uvx from Homebrew, nvm or uv aren't found. This takes
/// the PATH from the user's login shell once at startup; child processes inherit it.
/// </summary>
public static class LoginShellEnvironment
{
    public static void ApplyPath()
    {
        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (string.IsNullOrEmpty(shell) || !File.Exists(shell))
        {
            shell = "/bin/zsh";
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo(shell, ["-l", "-c", "printf %s \"$PATH\""])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
            });
            if (process is null)
            {
                return;
            }

            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            if (!process.WaitForExit(TimeSpan.FromSeconds(3)))
            {
                // A slow or interactive shell profile: keep the PATH we have.
                process.Kill(entireProcessTree: true);
                return;
            }

            var path = output.Result.Trim();
            if (process.ExitCode == 0 && path.Length > 0)
            {
                // Keep anything the app was started with that the shell doesn't have.
                var current = Environment.GetEnvironmentVariable("PATH")?.Split(':') ?? [];
                var merged = path.Split(':').Concat(current).Where(p => p.Length > 0).Distinct();
                Environment.SetEnvironmentVariable("PATH", string.Join(':', merged));
            }
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // No usable shell: MCP servers need full paths, as before.
        }
    }
}
