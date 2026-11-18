using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using SurveyJsAspNetCore.Configuration;

namespace SurveyJsAspNetCore.Relay;

/// <summary>
/// One fill-together room: the definition it hands out, the latest value of every answer (last write wins) and the
/// people in it. Messages from several sockets arrive at once, so every change takes the room's lock.
/// </summary>
public sealed class FillRoom(string key, JsonNode? seed, Dictionary<string, JsonNode?> values, FillRooms rooms)
{
    private readonly Lock gate = new();
    private readonly List<Participant> clients = [];
    private Dictionary<string, JsonNode?> values = values;

    public string Key { get; } = key;
    public JsonNode? Seed { get; } = seed;

    /// <summary>32 random hex characters per room instance: a client that reconnects can tell a new session.</summary>
    public string SessionId { get; } = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    /// <summary>The response saved for this room session, once someone finished the job sheet.</summary>
    public long? ResponseId { get; set; }

    /// <summary>Completing saves once: two people may finish at the same moment.</summary>
    public SemaphoreSlim Saving { get; } = new(1, 1);

    internal int Generation { get; private set; }
    internal bool Dropped { get; set; }

    public int Count { get { lock (gate) return clients.Count; } }

    /// <summary>Add a participant on the lowest colour slot nobody in the room holds.</summary>
    public void Join(Participant participant)
    {
        lock (gate)
        {
            var taken = clients.Select(c => c.Slot).ToHashSet();
            participant.Slot = Enumerable.Range(1, int.MaxValue - 1).First(slot => !taken.Contains(slot));
            clients.Add(participant);
            Generation++;
        }
        rooms.Changed(this);
    }

    /// <summary>Remove a participant and tell the others; the room is dropped 2 s after the last person leaves.</summary>
    public void Leave(Participant participant)
    {
        lock (gate)
        {
            clients.Remove(participant);
        }
        Broadcast(participant, new { type = "peer-left", clientId = participant.Id });
        rooms.Left(this);
    }

    /// <summary>Slots are shown as colours 1..9 (0 means unknown).</summary>
    public static int ColorIndex(int slot) => 1 + (slot - 1) % 9;

    public void SetValue(string key, JsonNode? value)
    {
        lock (gate)
        {
            values[key] = value;
        }
        rooms.Changed(this);
    }

    /// <summary>After the save: the room's answers are the saved response.</summary>
    public void ReplaceValues(JsonObject data)
    {
        lock (gate)
        {
            values = data.ToDictionary(entry => entry.Key, entry => entry.Value);
        }
        rooms.Changed(this);
    }

    /// <summary>The answers as a JSON object, even when empty.</summary>
    public Dictionary<string, JsonNode?> Snapshot()
    {
        lock (gate)
        {
            return new Dictionary<string, JsonNode?>(values);
        }
    }

    /// <summary>Everyone else who has shared a retained presence: { clientId, name, colorIndex, state }.</summary>
    public List<object> Roster(Participant except)
    {
        lock (gate)
        {
            return clients.Where(c => c != except && c.State is not null).Select(c => Peer(c, c.State)).ToList();
        }
    }

    public static object Peer(Participant client, object? state) =>
        new { clientId = client.Id, name = client.Name, colorIndex = ColorIndex(client.Slot), state };

    /// <summary>
    /// Send to everyone in the room except the sender: nobody gets their own echo. A non-retained presence update
    /// (a cursor move) may be skipped for a peer that is falling behind.
    /// </summary>
    public void Broadcast(Participant from, object message, bool droppable = false)
    {
        var json = Participant.Serialize(message);
        foreach (var client in Clients().Where(c => c != from && !(droppable && c.Pending > 64)))
        {
            client.Send(json);
        }
    }

    /// <summary>Send to everyone, the sender included.</summary>
    public void SendToAll(object message)
    {
        var json = Participant.Serialize(message);
        foreach (var client in Clients())
        {
            client.Send(json);
        }
    }

    private List<Participant> Clients()
    {
        lock (gate)
        {
            return [.. clients];
        }
    }
}

/// <summary>The fill-together rooms of this app process, by room key.</summary>
public sealed class FillRooms(IOptions<SurveyJsOptions> options)
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, Task<FillRoom>> rooms = [];

    /// <summary>The room for this key, seeded (the definition and the saved answers) when nobody is in it yet.</summary>
    public async Task<FillRoom> OpenAsync(string key, Func<Task<(JsonNode? Seed, Dictionary<string, JsonNode?> Values)>> seed)
    {
        Task<FillRoom> room;
        lock (gate)
        {
            if (!rooms.TryGetValue(key, out room!) || room is { IsCompletedSuccessfully: true, Result.Dropped: true })
            {
                room = CreateAsync(key, seed);
                rooms[key] = room;
            }
        }
        try
        {
            return await room;
        }
        catch
        {
            lock (gate)
            {
                if (rooms.TryGetValue(key, out var current) && current == room) rooms.Remove(key);
            }
            throw;
        }
    }

    private async Task<FillRoom> CreateAsync(string key, Func<Task<(JsonNode? Seed, Dictionary<string, JsonNode?> Values)>> seed)
    {
        var (definition, values) = await seed();
        return new FillRoom(key, definition, values, this);
    }

    internal void Left(FillRoom room)
    {
        Changed(room);
        if (room.Count == 0)
        {
            _ = DropLaterAsync(room, room.Generation);
        }
    }

    /// <summary>Back within 2 s: keep the room (a reload, a reconnect).</summary>
    private async Task DropLaterAsync(FillRoom room, int generation)
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        lock (gate)
        {
            if (room.Count == 0 && room.Generation == generation && rooms.TryGetValue(room.Key, out var current)
                && current.IsCompletedSuccessfully && current.Result == room)
            {
                room.Dropped = true;
                rooms.Remove(room.Key);
            }
        }
    }

    /// <summary>Demo only: the room's latest values for the "What the server stored" panel.</summary>
    internal void Changed(FillRoom room)
    {
        if (options.Value.DemoMode)
        {
            RoomKey.WriteSnapshot(options.Value.DataDir, room.Key, "fill", new()
            {
                ["participants"] = room.Count,
                ["values"] = room.Snapshot(),
                ["responseId"] = room.ResponseId,
            });
        }
    }
}
