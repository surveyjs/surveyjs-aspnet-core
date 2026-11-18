using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using SurveyJsAspNetCore.Data;
using SurveyJsAspNetCore.Relay;

namespace SurveyJsAspNetCore.Endpoints;

/// <summary>
/// Edit Forms Together: the relay behind CollaborationPlugin from survey-creator-core/collaboration, a WebSocket
/// endpoint in this app. Wire protocol: https://github.com/surveyjs/collaborative-form-editing (PROTOCOL.md).
/// The rooms are in Relay/EditRoom.cs. Saving is unchanged: Creator's Save button calls PUT /api/forms/{id}.
/// </summary>
public static class EditTogether
{
    public static IEndpointRouteBuilder MapEditTogether(this IEndpointRouteBuilder app)
    {
        #region sjs:edit-together.server
        // The same kind of relay as Fill Forms Together, keeping an append-only log instead of a value map:
        // Creator records must be replayed in order
        app.Map("/ws/forms/{formId:regex(^[A-Za-z0-9_-]{{1,64}}$)}", async (string formId, string? name, HttpContext context,
            IAuthorizationService auth, EditRooms rooms, RequestStorage storage) =>
        {
            if (!context.WebSockets.IsWebSocketRequest) return Results.Json(new { error = "Expected a WebSocket request" }, statusCode: 400);
            // Demo only: the demo_user cookie stands in for your real session check here. Editing is for editors (edit-forms)
            if (!(await auth.AuthorizeAsync(context.User, "edit-forms")).Succeeded)
                return Results.Json(new { error = "You don't have editor rights: only editors can change forms" }, statusCode: 403);
            var database = storage.DatabasePath;
            var room = await rooms.OpenAsync(RoomKey.For(storage, formId), () => SavedDefinitionAsync(database, formId));   // seed = saved definition

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await using var me = new Participant(socket, DisplayName(name, context.User.Identity!.Name!));
            room.Join(me);
            me.Send(new { type = "init", clientId = me.Id, colorIndex = me.Slot, color = EditRoom.Palette[me.Slot], seed = room.Seed, log = room.Log() });
            if (room.Roster(me) is { Count: > 0 } peers) me.Send(new { type = "presence-sync", peers });
            try
            {
                await foreach (var raw in me.ReceiveAsync(context.RequestAborted))
                {
                    var message = Parse(raw);                                 // records stay opaque: {} is relayed as {}
                    switch (message?["type"] is JsonValue type && type.TryGetValue(out string? kind) ? kind : null)
                    {
                        case "append" when message!["payload"] is not null:  // append, then fan out
                            room.Append(me, message["payload"]);
                            break;
                        case "presence" when message!["state"] is not null && raw.Length <= 4096:
                            me.State = message["state"];
                            room.Broadcast(me, new { type = "presence", peer = EditRoom.Peer(me) });
                            break;
                    }                                                         // unknown types: ignore
                }
            }
            finally
            {
                room.Leave(me);                                               // presence-leave; the log lives until the room empties
            }
            return Results.Empty;
        });
        #endregion
        return app;
    }

    /// <summary>The definition saved through creator-load-save, or an empty one for a new form.</summary>
    private static async Task<JsonNode> SavedDefinitionAsync(string database, string formId)
    {
        await using var db = SurveyDbSetup.Open(database);
        var json = await db.Forms.Where(f => f.Key == formId).Select(f => f.Json).SingleOrDefaultAsync();
        return (json is null ? null : JsonNode.Parse(json)) ?? new JsonObject();
    }

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
}
