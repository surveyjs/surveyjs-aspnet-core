using System.Diagnostics;
using System.Net.Sockets;

namespace SurveyJsAspNetCore.Configuration;

/// <summary>Running it locally: the port check and the build line that `dotnet run` prints and the pages show.</summary>
public static class LocalRun
{
    /// <summary>
    /// True when anything answers on 127.0.0.1:port. Checked before the app starts: don't rely on the bind error,
    /// because on Windows a second server can start on a busy port and the first one keeps answering.
    /// </summary>
    public static bool IsBusy(int port)
    {
        using var client = new TcpClient();
        try
        {
            return client.ConnectAsync("127.0.0.1", port).Wait(TimeSpan.FromMilliseconds(500)) && client.Connected;
        }
        catch (AggregateException)
        {
            return false;   // refused: the port is free
        }
    }

    public static string BusyMessage(int port) =>
        $"Port {port} is already in use. Another copy of this app (another checkout or branch?) may be running. Stop it, or set APP_PORT.";

    /// <summary>The checkout's `&lt;branch&gt;@&lt;short commit&gt;`, read from git once (2 s at most), or null without git.</summary>
    public static string? GitBuild(string folder)
    {
        var branch = Git(folder, "rev-parse --abbrev-ref HEAD");
        var commit = branch is null ? null : Git(folder, "rev-parse --short HEAD");
        return branch is null || commit is null ? null : $"{branch}@{commit}";
    }

    private static string? Git(string folder, string arguments)
    {
        try
        {
            using var git = Process.Start(new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = folder, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            });
            if (git is null)
            {
                return null;
            }
            var output = git.StandardOutput.ReadToEndAsync();
            if (!git.WaitForExit(2000))
            {
                git.Kill();
                return null;
            }
            return git.ExitCode == 0 && output.Wait(500) ? output.Result.Trim() : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;   // no git on PATH
        }
    }
}
