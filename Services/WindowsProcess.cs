using System.Diagnostics;
using System.Text;

namespace Pulse.Services;

internal static class WindowsProcess
{
    public static async Task<string> RunAsync(string executable, TimeSpan timeout, params string[] arguments)
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, executable))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        using var cancel = new CancellationTokenSource(timeout);
        try { await process.WaitForExitAsync(cancel.Token); }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{Path.GetFileName(executable)} did not finish within {timeout.TotalSeconds:0} seconds.");
        }

        if (process.ExitCode != 0)
        {
            var detail = (await errors).Trim();
            if (detail.Length == 0)
                detail = (await output).Trim();
            if (detail.Length > 500)
                detail = detail[..500] + "…";
            throw new InvalidOperationException(detail.Length == 0 ? $"{Path.GetFileName(executable)} exited with code {process.ExitCode}." : detail);
        }
        return (await output).Trim();
    }

    public static Task<string> PowerShellAsync(string script, int timeoutSeconds)
    {
        var command = Convert.ToBase64String(Encoding.Unicode.GetBytes(
            "$ErrorActionPreference = 'Stop'; $ProgressPreference = 'SilentlyContinue'; " +
            "[Console]::OutputEncoding = [Text.Encoding]::UTF8; " + script));
        return RunAsync(@"WindowsPowerShell\v1.0\powershell.exe", TimeSpan.FromSeconds(timeoutSeconds),
            "-NoProfile", "-NonInteractive", "-EncodedCommand", command);
    }
}
