namespace VoxBridge.Contract;

/// <summary><c>GET /v1/models</c> の 1 件。</summary>
public sealed record ModelEntry(string Id, string Object, long Created, string OwnedBy);

/// <summary><c>GET /v1/audio/voices</c> の 1 件。</summary>
public sealed record VoiceEntry(string Id, string Object, string DisplayName, string Language, string Gender);

/// <summary>OpenAI 互換の一覧レスポンス。</summary>
public sealed record ListResponse<T>(string Object, IReadOnlyList<T> Data)
{
    public static ListResponse<T> Of(IReadOnlyList<T> data) => new("list", data);
}

public static class ListResponses
{
    /// <summary>
    /// <c>/v1/models</c> の総称 id。声を指定しないときはこれを使う。
    /// </summary>
    public const string GenericModelId = "windows-speech";

    public const string Owner = "voxbridge";

    public static ModelEntry Model(string id) => new(id, "model", 0, Owner);

    public static VoiceEntry Voice(string id, string displayName, string language, string gender) =>
        new(id, "voice", displayName, language, gender);
}
