using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using SurveyJsAspNetCore.Integration;

namespace SurveyJsAspNetCore.Relay;

/// <summary>
/// One person in a relay room: their WebSocket, id, name, colour slot and presence. WebSocket.SendAsync must not run
/// concurrently on one socket, and a room's messages arrive from several sockets at once, so sends go through a queue
/// that one loop drains.
/// </summary>
public sealed class Participant : IAsyncDisposable
{
    /// <summary>A large answer (a signature, a long text) fits in one message.</summary>
    private const int MaxMessageBytes = 20 * 1024 * 1024;

    private readonly WebSocket socket;
    private readonly Channel<string> outbox = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Task sending;

    public Participant(WebSocket socket, string name)
    {
        this.socket = socket;
        Name = name;
        sending = SendLoopAsync();
    }

    public string Id { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
    public string Name { get; }
    public int Slot { get; set; }

    /// <summary>The last presence state this participant shared, or null.</summary>
    public object? State { get; set; }

    /// <summary>Messages waiting to go out: a peer falling behind may skip cursor moves.</summary>
    public int Pending => outbox.Reader.Count;

    public void Send(string json) => outbox.Writer.TryWrite(json);

    public void Send(object message) => Send(Serialize(message));

    public static string Serialize(object message) => JsonSerializer.Serialize(message, StoredJson.Options);

    /// <summary>Each text message the client sends, until it closes the connection or the request is aborted.</summary>
    public async IAsyncEnumerable<string> ReceiveAsync([EnumeratorCancellation] CancellationToken cancellation)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult received;
            try
            {
                received = await socket.ReceiveAsync(buffer, cancellation);
            }
            catch (Exception e) when (e is WebSocketException or OperationCanceledException)
            {
                yield break;   // the connection dropped
            }
            if (received.MessageType == WebSocketMessageType.Close)
            {
                yield break;
            }
            message.Write(buffer, 0, received.Count);
            if (message.Length > MaxMessageBytes)
            {
                await CloseAsync(WebSocketCloseStatus.MessageTooBig);
                yield break;
            }
            if (!received.EndOfMessage)
            {
                continue;
            }
            if (received.MessageType == WebSocketMessageType.Text)
            {
                yield return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
            }
            message.SetLength(0);
        }
    }

    private async Task SendLoopAsync()
    {
        try
        {
            await foreach (var json in outbox.Reader.ReadAllAsync())
            {
                if (socket.State != WebSocketState.Open)
                {
                    return;
                }
                await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
            }
        }
        catch (WebSocketException)
        {
            // the connection dropped: the receive loop ends too
        }
    }

    private async Task CloseAsync(WebSocketCloseStatus status)
    {
        try
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseAsync(status, null, timeout.Token);
            }
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException)
        {
            // already gone
        }
    }

    /// <summary>Send what is queued, then close the connection.</summary>
    public async ValueTask DisposeAsync()
    {
        outbox.Writer.TryComplete();
        await Task.WhenAny(sending, Task.Delay(TimeSpan.FromSeconds(2)));
        await CloseAsync(WebSocketCloseStatus.NormalClosure);
    }
}
