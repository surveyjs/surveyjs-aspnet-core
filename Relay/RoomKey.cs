using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SurveyJsAspNetCore.Data;
using SurveyJsAspNetCore.Integration;

namespace SurveyJsAspNetCore.Relay;

/// <summary>
/// Demo only: in demo mode a room belongs to one visitor sandbox, so its key is "&lt;demo_sid&gt;:&lt;id&gt;" and visitors in
/// different sandboxes never share a room. Outside demo mode the key is the plain record or form id.
/// </summary>
public static class RoomKey
{
    public static string For(RequestStorage storage, string id) => For(storage.SandboxId, id);

    public static string For(string? sandboxId, string id) => sandboxId is null ? id : $"{sandboxId}:{id}";

    /// <summary>
    /// Where a relay writes a room snapshot for the Data panel. Room keys contain ":" (invalid in Windows file names)
    /// and ids from the URL, so the file is named by the key's SHA-256.
    /// </summary>
    public static string SnapshotPath(string dataDir, string roomKey, string relay = "fill")
    {
        var name = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(roomKey))) + ".json";
        return Path.Combine(dataDir, "relay", relay == "fill" ? name : $"{relay}-{name}");
    }

    private static readonly JsonSerializerOptions Indented = new(StoredJson.Options) { WriteIndented = true };

    /// <summary>Demo only: write a room's state for the "What the server stored" panel.</summary>
    public static void WriteSnapshot(string dataDir, string roomKey, string relay, Dictionary<string, object?> snapshot)
    {
        var path = SnapshotPath(dataDir, roomKey, relay);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        snapshot["updatedAt"] = StoredJson.Now();
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, Indented));
    }
}
