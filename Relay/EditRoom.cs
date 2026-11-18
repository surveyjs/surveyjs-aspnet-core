using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using SurveyJsAspNetCore.Configuration;

namespace SurveyJsAspNetCore.Relay;

/// <summary>
/// One edit-together room: the saved definition it started from, every Creator record since (in arrival order, never
/// deduplicated) and the people in it. Every change takes the room's lock.
/// </summary>
public sealed class EditRoom(string key, JsonNode seed, EditRooms rooms)
{
    /// <summary>Colour slots, skipping 5 (yellow is unreadable with white text); 0 is gray and never assigned.</summary>
    private static readonly int[] Slots = [1, 2, 3, 4, 6, 7, 8, 9];

    /// <summary>The colour of each slot: the same values as survey-core's --sjs2-color-utility-user-bg-color-N.</summary>
    public static readonly string[] Palette = ["#808080", "#1570EF", "#CA4FFB", "#19B35C", "#19B394", "#F9C50B", "#F99130", "#F1529C", "#02ADEB", "#4E6198"];

    private readonly Lock gate = new();
    private readonly List<Participant> clients = [];
    private readonly List<JsonNode?> log = [];

    public string Key { get; } = key;
    public JsonNode Seed { get; } = seed;

    internal int Generation { get; private set; }
    internal bool Dropped { get; set; }

    public int Count { get { lock (gate) return clients.Count; } }

    public void Join(Participant participant)
    {
        lock (gate)
        {
            var taken = clients.Select(c => c.Slot).ToHashSet();
            participant.Slot = Slots.FirstOrDefault(slot => !taken.Contains(slot), Slots[clients.Count % Slots.Length]);
            clients.Add(participant);
            Generation++;
        }
        rooms.Changed(this);
    }

    public void Leave(Participant participant)
    {
        lock (gate)
        {
            clients.Remove(participant);
        }
        Broadcast(participant, new { type = "presence-leave", clientId = participant.Id });
        rooms.Left(this);
    }

    /// <summary>Every record since the saved definition, in order: a late joiner replays them.</summary>
    public List<JsonNode?> Log()
    {
        lock (gate)
        {
            return [.. log];
        }
    }

    /// <summary>Append a record and send it to everyone else, in one step, so every peer gets the log's order.</summary>
    public void Append(Participant from, JsonNode? payload)
    {
        List<Participant> others;
        lock (gate)
        {
            log.Add(payload);
            others = clients.Where(c => c != from).ToList();
            var json = Participant.Serialize(new { type = "record", from = from.Id, payload });
            others.ForEach(client => client.Send(json));
        }
        rooms.Changed(this);
    }

    /// <summary>Everyone else who has shared their presence: { clientId, name, colorIndex, color, state }.</summary>
    public List<object> Roster(Participant except)
    {
        lock (gate)
        {
            return clients.Where(c => c != except && c.State is not null).Select(Peer).ToList();
        }
    }

    /// <summary>Creator's plugin paints rings and cursors from color, so each peer carries it next to colorIndex.</summary>
    public static object Peer(Participant client) =>
        new { clientId = client.Id, name = client.Name, colorIndex = client.Slot, color = Palette[client.Slot], state = client.State };

    /// <summary>Send to everyone in the room except the sender.</summary>
    public void Broadcast(Participant from, object message)
    {
        var json = Participant.Serialize(message);
        List<Participant> others;
        lock (gate)
        {
            others = clients.Where(c => c != from).ToList();
        }
        others.ForEach(client => client.Send(json));
    }
}

/// <summary>The edit-together rooms of this app process, by room key.</summary>
public sealed class EditRooms(IOptions<SurveyJsOptions> options)
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, Task<EditRoom>> rooms = [];

    /// <summary>The room for this key, seeded with the saved definition when nobody is in it yet.</summary>
    public async Task<EditRoom> OpenAsync(string key, Func<Task<JsonNode>> seed)
    {
        Task<EditRoom> room;
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

    private async Task<EditRoom> CreateAsync(string key, Func<Task<JsonNode>> seed) => new(key, await seed(), this);

    internal void Left(EditRoom room)
    {
        Changed(room);
        if (room.Count == 0)
        {
            _ = DropLaterAsync(room, room.Generation);
        }
    }

    /// <summary>The log lives until the room empties; back within 2 s keeps it.</summary>
    private async Task DropLaterAsync(EditRoom room, int generation)
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

    /// <summary>Demo only: the room's state for the "What the server stored" panel.</summary>
    internal void Changed(EditRoom room)
    {
        if (options.Value.DemoMode)
        {
            RoomKey.WriteSnapshot(options.Value.DataDir, room.Key, "edit", new()
            {
                ["participants"] = room.Count,
                ["seed title"] = room.Seed is JsonObject definition ? definition["title"] : null,
                ["records in the log"] = room.Log().Count,
            });
        }
    }
}
