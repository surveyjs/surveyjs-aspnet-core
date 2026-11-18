using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SurveyJsAspNetCore.Data;
using SurveyJsAspNetCore.Integration;
using SurveyJsAspNetCore.Relay;

namespace SurveyJsAspNetCore.Endpoints;

/// <summary>
/// Fill Forms Together: the relay behind CollaborationPlugin from survey-core/collaboration, a WebSocket endpoint in
/// this app (one port for everything). Wire protocol: https://github.com/surveyjs/collaborative-form-filling (PROTOCOL.md).
/// The rooms are in Relay/FillRoom.cs.
/// </summary>
public static class FillTogether
{
    public static IEndpointRouteBuilder MapFillTogether(this IEndpointRouteBuilder app)
    {
        #region sjs:fill-together.server
        // A WebSocket endpoint relays JSON between the people filling one record: answers, focus and cursors
        app.Map("/ws/rooms/{recordId:regex(^[A-Za-z0-9_-]{{1,64}}$)}", async (string recordId, string? name, HttpContext context,
            FillRooms rooms, RequestStorage storage, ILogger<FillRoom> log) =>
        {
            if (!context.WebSockets.IsWebSocketRequest) return Results.Json(new { error = "Expected a WebSocket request" }, statusCode: 400);
            // Demo only: the demo_user cookie stands in for your real session check here (may this user open this record?)
            if (context.User.Identity?.IsAuthenticated != true) return Results.Json(new { error = "Sign in to fill this record" }, statusCode: 403);
            var database = storage.DatabasePath;
            var room = await rooms.OpenAsync(RoomKey.For(storage, recordId), () => SeedAsync(database, recordId));   // the definition + the saved answers

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await using var me = new Participant(socket, DisplayName(name, context.User.Identity.Name!));
            room.Join(me);
            me.Send(new
            {
                type = "init", clientId = me.Id, name = me.Name, colorIndex = FillRoom.ColorIndex(me.Slot), seed = room.Seed,
                values = room.Snapshot(), peers = room.Roster(me), sessionId = room.SessionId, responseId = room.ResponseId,
            });
            try
            {
                await foreach (var raw in me.ReceiveAsync(context.RequestAborted))
                {
                    var message = Parse(raw);                                 // objects stay objects: {} is relayed as {}
                    switch (Text(message?["type"]))
                    {
                        case "complete" when message!["data"] is JsonObject data:
                            await CompleteAsync(room, me, data, database, log);
                            break;
                        case "value" when Text(message!["key"]) is { } key && room.ResponseId is null:
                            room.SetValue(key, message["value"]);              // last write wins
                            room.Broadcast(me, new { type = "value", from = me.Id, key, value = message["value"] });
                            break;
                        case "presence" when message!["state"] is not null && raw.Length <= 4096:   // stamp identity, relay
                            var retain = message["retain"]?.GetValueKind() != JsonValueKind.False;
                            if (retain) me.State = message["state"];
                            room.Broadcast(me, new { type = "peer", retain, peer = FillRoom.Peer(me, message["state"]) }, droppable: !retain);
                            break;
                    }                                                         // unknown types: ignore
                }
            }
            finally
            {
                room.Leave(me);                                               // peer-left; the room is dropped 2 s after the last person leaves
            }
            return Results.Empty;
        });
        #endregion
        return app;
    }

    #region sjs:fill-together.complete
    // Save once per room session and send the same confirmation to every participant
    private static async Task CompleteAsync(FillRoom room, Participant from, JsonObject data, string database, ILogger log)
    {
        await room.Saving.WaitAsync();                                       // two people may finish at the same moment
        try
        {
            if (room.ResponseId is null)
            {
                await using var db = SurveyDbSetup.Open(database);           // a context of its own: the socket outlives the request
                room.ResponseId = await StoredJson.InsertResponseAsync(db, "job-sheet", data);
                room.ReplaceValues(data);
            }
            room.SendToAll(new { type = "completed", id = room.ResponseId, data = room.Snapshot() });
        }
        catch (Exception e) when (e is DbUpdateException or InvalidOperationException or IOException)
        {
            log.LogError(e, "Couldn't save the job sheet of room {Room}", room.Key);
            from.Send(new { type = "save-error", error = "Couldn't save the job sheet. Please try again." });
        }
        finally
        {
            room.Saving.Release();
        }
    }
    #endregion

    /// <summary>The job sheet definition, and the saved answers when the record exists (otherwise a blank sheet).</summary>
    private static async Task<(JsonNode? Seed, Dictionary<string, JsonNode?> Values)> SeedAsync(string database, string recordId)
    {
        await using var db = SurveyDbSetup.Open(database);
        var definition = await db.Forms.Where(f => f.Key == "job-sheet").Select(f => f.Json).SingleOrDefaultAsync();
        var data = long.TryParse(recordId, out var id)
            ? await db.Responses.Where(r => r.FormId == "job-sheet" && r.Id == id).Select(r => r.Data).SingleOrDefaultAsync()
            : null;
        var values = (data is null ? null : JsonNode.Parse(data) as JsonObject)?.ToDictionary(entry => entry.Key, entry => entry.Value) ?? [];
        return (definition is null ? new JsonObject() : JsonNode.Parse(definition), values);
    }

    /// <summary>The display name from ?name=, trimmed to 32 characters, or the user's name.</summary>
    private static string DisplayName(string? name, string fallback) =>
        string.IsNullOrWhiteSpace(name) ? fallback : name.Trim()[..Math.Min(name.Trim().Length, 32)];

    private static JsonObject? Parse(string raw)
    {
        try
        {
            return JsonNode.Parse(raw) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
}
