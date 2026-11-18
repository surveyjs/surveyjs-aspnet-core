using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace SurveyJsAspNetCore.Tests;

/// <summary>The app refuses a busy APP_PORT before starting, with the message the README shows.</summary>
public sealed class RunCommandTests
{
    [Fact]
    public async Task TheAppRefusesABusyPort()
    {
        using var busy = new TcpListener(IPAddress.Loopback, 0);
        busy.Start();
        var port = ((IPEndPoint)busy.LocalEndpoint).Port;

        var dll = Path.Combine(AppContext.BaseDirectory, "surveyjs-aspnet-core.dll");
        var info = new ProcessStartInfo("dotnet", [dll])
        {
            WorkingDirectory = ExamplePagesTests.Root,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            Environment =
            {
                ["APP_PORT"] = port.ToString(),
                ["DATA_DIR"] = Path.Combine(Path.GetTempPath(), "surveyjs-tests", Guid.NewGuid().ToString("N")),
                ["ASPNETCORE_ENVIRONMENT"] = "Production",
            },
        };
        using var app = Process.Start(info)!;
        var error = await app.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await app.WaitForExitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, app.ExitCode);
        Assert.Contains($"Port {port} is already in use. Another copy of this app (another checkout or branch?) may be running. Stop it, or set APP_PORT.", error);
    }
}
