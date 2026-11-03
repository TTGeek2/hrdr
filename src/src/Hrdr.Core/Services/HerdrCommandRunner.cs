using System.ComponentModel;
using System.Diagnostics;

namespace Hrdr.Core.Services;

public record HerdrCommandResult(int ExitCode, string Stdout, string Stderr);

public interface IHerdrCommandRunner
{
    Task<HerdrCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken ct);
}

public sealed class ProcessHerdrCommandRunner : IHerdrCommandRunner
{
    public async Task<HerdrCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken ct)
    {
        var start = new ProcessStartInfo
        {
            FileName = ResolveHerdrPath(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
                throw new HerdrLaunchException("Could not start herdr.");
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            throw new HerdrLaunchException("herdr was not found on this machine.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryKill(process);
            await DrainAsync(stdoutTask, stderrTask);
            throw new HerdrLaunchException("herdr timed out.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await DrainAsync(stdoutTask, stderrTask);
            throw;
        }

        return new HerdrCommandResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    public static string ResolveHerdrPath()
    {
        var configured = Environment.GetEnvironmentVariable("HERDR_BIN_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Path.Combine(home, ".local", "bin", "herdr");
        if (File.Exists(local))
            return local;

        return "herdr";
    }

    private static async Task DrainAsync(Task<string> stdout, Task<string> stderr)
    {
        try
        {
            await Task.WhenAll(stdout, stderr);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
