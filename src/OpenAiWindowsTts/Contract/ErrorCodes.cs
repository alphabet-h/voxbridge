using System.Reflection;

namespace OpenAiWindowsTts.Contract;

/// <summary>
/// <c>error.code</c> の唯一の出典。
///
/// <c>docs/02-http-contract.md</c> §6 の表と <c>scripts/check-contract.mjs</c> が機械照合する。
/// コード文字列を他所に直書きすると、表と実装が静かにずれる。増やすときは
/// <b>この class と docs/02 の表の両方</b>を直すこと（片方だけだと検査が落ちる）。
/// </summary>
public static class ErrorCodes
{
    /// <summary>input が空。</summary>
    public const string EmptyInput = "EMPTY_INPUT";

    /// <summary>リクエスト JSON を解釈できない。</summary>
    public const string InvalidJson = "INVALID_JSON";

    /// <summary>speed が受け付けられる範囲の外。</summary>
    public const string InvalidSpeed = "INVALID_SPEED";

    /// <summary>response_format が wav 以外。</summary>
    public const string UnsupportedFormat = "UNSUPPORTED_FORMAT";

    /// <summary>voice / model で指定された声がこの PC に無い。<c>none</c> と空文字はここに来ない。</summary>
    public const string VoiceNotFound = "VOICE_NOT_FOUND";

    /// <summary>リクエスト本文が大きすぎる。</summary>
    public const string PayloadTooLarge = "PAYLOAD_TOO_LARGE";

    /// <summary>そのメソッドとパスは実装していない。</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>実行枠が空かない。503 と組で返す。</summary>
    public const string EngineBusy = "ENGINE_BUSY";

    /// <summary>合成そのものが失敗した。500 と組で返す。</summary>
    public const string EngineInternal = "ENGINE_INTERNAL";

    /// <summary>
    /// 定義済みコードの全件。const から反射で組み立てるので、
    /// 「const は足したが一覧に入れ忘れた」が起こらない。
    /// </summary>
    public static IReadOnlyList<string> All { get; } = typeof(ErrorCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .Order(StringComparer.Ordinal)
        .ToArray();
}
