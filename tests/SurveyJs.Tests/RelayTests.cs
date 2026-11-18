using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.TestHost;
using SurveyJsAspNetCore.Demo;
using static SurveyJsAspNetCore.Tests.Http;

namespace SurveyJsAspNetCore.Tests;

/// <summary>The relays in the app process, through TestServer's WebSocket client.</summary>
public sealed class RelayTests
{
    private sealed class Peer(WebSocket socket) : IAsyncDisposable
    {
        public async Task SendAsync(string json) =>
            await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, Token);

        public async Task<JsonObject> ReceiveAsync(string? type = null)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (true)
            {
                var buffer = new byte[1 << 20];
                var received = await socket.ReceiveAsync(buffer, timeout.Token);
                if (received.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("closed");
                var message = JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, received.Count))!.AsObject();
                if (type is null || (string?)message["type"] == type) return message;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            }
            socket.Dispose();
        }
    }

    private static async Task<Peer> ConnectAsync(TestApp app, string path, string user = "alice", string? sandbox = null)
    {
        var client = app.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Cookie = sandbox is null ? $"demo_user={user}" : $"demo_user={user}; demo_sid={sandbox}";
        return new Peer(await client.ConnectAsync(new Uri(app.Server.BaseAddress, path.Replace("http", "ws")), Token));
    }

    [Fact]
    public async Task FillInitHasEveryFieldAndValuesReachOthers()
    {
        await using var app = new TestApp();
        var record = app.Sql("SELECT id FROM responses WHERE form_id = 'job-sheet' ORDER BY id LIMIT 1").Single();
        await using var first = await ConnectAsync(app, $"/ws/rooms/{record}?name=Dispatcher");
        var init = await first.ReceiveAsync();
        Assert.Equal("init", (string?)init["type"]);
        foreach (var field in new[] { "clientId", "name", "colorIndex", "seed", "values", "peers", "sessionId", "responseId" })
        {
            Assert.True(init.ContainsKey(field), $"init has no {field}");
        }
        Assert.Equal("Dispatcher", (string?)init["name"]);
        Assert.Matches("^[0-9a-f]{32}$", (string?)init["sessionId"]);
        Assert.NotNull(init["values"]!["customer"]);

        await using var second = await ConnectAsync(app, $"/ws/rooms/{record}?name=Technician");
        await second.ReceiveAsync("init");
        await first.SendAsync("""{"type":"value","key":"findings","value":{"a":1.0,"b":{}}}""");
        var value = await second.ReceiveAsync("value");
        Assert.Equal("""{"a":1.0,"b":{}}""", value["value"]!.ToJsonString());
        await first.SendAsync("""{"type":"no-such-type"}""");
        await first.SendAsync("""{"type":"presence","state":{"focus":"findings"}}""");
        Assert.Equal("peer", (string?)(await second.ReceiveAsync("peer"))["type"]);
    }

    [Fact]
    public async Task TwoCompletesAtOnceSaveOneResponseForEveryone()
    {
        await using var app = new TestApp();
        await using var first = await ConnectAsync(app, "/ws/rooms/new-sheet");
        await using var second = await ConnectAsync(app, "/ws/rooms/new-sheet");
        await first.ReceiveAsync("init");
        await second.ReceiveAsync("init");
        var before = int.Parse(app.Sql("SELECT COUNT(*) FROM responses WHERE form_id = 'job-sheet'").Single()!);

        await Task.WhenAll(first.SendAsync("""{"type":"complete","data":{"customer":"A","hours":2.0}}"""),
                           second.SendAsync("""{"type":"complete","data":{"customer":"B"}}"""));
        var one = await first.ReceiveAsync("completed");
        var two = await second.ReceiveAsync("completed");
        Assert.Equal((long)one["id"]!, (long)two["id"]!);
        Assert.Equal(before + 1, int.Parse(app.Sql("SELECT COUNT(*) FROM responses WHERE form_id = 'job-sheet'").Single()!));
        Assert.Equal(one["data"]!.ToJsonString(), app.Sql($"SELECT data FROM responses WHERE id = {one["id"]}").Single());
    }

    [Fact]
    public async Task EditInitCarriesTheLogAndPresenceSyncFollows()
    {
        await using var app = new TestApp();
        await using var first = await ConnectAsync(app, "/ws/forms/support-team?name=Ann");
        var init = await first.ReceiveAsync();
        Assert.Equal("init", (string?)init["type"]);
        Assert.Empty(init["log"]!.AsArray());
        await first.SendAsync("""{"type":"presence","state":{"selection":"q1"}}""");
        await first.SendAsync("""{"type":"append","payload":{"seq":1,"op":{}}}""");
        await Task.Delay(200, Token);

        await using var second = await ConnectAsync(app, "/ws/forms/support-team?name=Ben");
        var late = await second.ReceiveAsync("init");
        Assert.Equal("""[{"seq":1,"op":{}}]""", late["log"]!.ToJsonString());
        var sync = await second.ReceiveAsync("presence-sync");
        Assert.Equal("Ann", (string?)sync["peers"]![0]!["name"]);
    }

    [Theory]
    [InlineData("bob")]
    [InlineData("none")]
    public async Task TheEditRelayAdmitsEditorsOnly(string user)
    {
        await using var app = new TestApp();
        await Assert.ThrowsAnyAsync<Exception>(() => ConnectAsync(app, "/ws/forms/support-team", user));
    }

    [Fact]
    public async Task SandboxesGetSeparateRooms()
    {
        await using var app = new TestApp(new() { ["DEMO_MODE"] = "true" });
        string a = Sandboxes.NewId(), b = Sandboxes.NewId();
        // A page request creates each sandbox, as a visitor's first page does
        await app.As("alice", a).GetAsync("/examples/fill-together", Token);
        await app.As("alice", b).GetAsync("/examples/fill-together", Token);

        await using var inA = await ConnectAsync(app, "/ws/rooms/shared-id", sandbox: a);
        await inA.ReceiveAsync("init");
        await inA.SendAsync("""{"type":"value","key":"customer","value":"only in A"}""");
        await Task.Delay(200, Token);
        await using var inB = await ConnectAsync(app, "/ws/rooms/shared-id", sandbox: b);
        Assert.Equal("{}", (await inB.ReceiveAsync("init"))["values"]!.ToJsonString());
        await using var alsoA = await ConnectAsync(app, "/ws/rooms/shared-id", sandbox: a);
        Assert.Equal("only in A", (string?)(await alsoA.ReceiveAsync("init"))["values"]!["customer"]);
    }
}
