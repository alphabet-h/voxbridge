using System.Text.Json;
using System.Text.Json.Serialization;

namespace VoxBridge.Contract;

/// <summary>
/// <c>POST /v1/audio/speech</c> のリクエスト。
///
/// <b>ここに書いていないフィールドは読まない。</b> 参照音声・caption・seed・ステップ数などは
/// 「拾って捨てる」のではなく、そもそも拾わない（<c>System.Text.Json</c> は未知メンバを
/// 既定で無視する）。拡張オブジェクトのキー名を探す必要すらない。
///
/// エラーにしないのは、そうしないと**利用者が参照音声を 1 つ設定しただけで生成が全件失敗する**ため
/// （<c>docs/01</c> §3）。
/// </summary>
public sealed class SpeechRequest
{
    public string? Input { get; init; }

    public string? Model { get; init; }

    /// <summary>文字列と <c>{"id": "..."}</c> の両方が来る。</summary>
    [JsonConverter(typeof(VoiceSelectorConverter))]
    public string? Voice { get; init; }

    public string? ResponseFormat { get; init; }

    public double? Speed { get; init; }

    /// <summary>受けるだけ。<c>sse</c> でもバイナリを返す（<c>docs/02</c> §4.2）。</summary>
    public string? StreamFormat { get; init; }
}

/// <summary><c>voice</c> が文字列でもオブジェクトでも読めるようにする。</summary>
public sealed class VoiceSelectorConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();

            case JsonTokenType.StartObject:
                return ReadId(ref reader);

            default:
                // 数値や配列が来ても落とさない。「指定なし」として扱う
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value);
    }

    private static string? ReadId(ref Utf8JsonReader reader)
    {
        string? id = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                reader.Skip();
                continue;
            }

            var name = reader.GetString();
            reader.Read();

            if (id is null && string.Equals(name, "id", StringComparison.OrdinalIgnoreCase)
                && reader.TokenType == JsonTokenType.String)
            {
                id = reader.GetString();
            }
            else
            {
                reader.Skip();
            }
        }

        return id;
    }
}
