namespace OpenAiWindowsTts.Contract;

/// <summary>検証を通ったリクエスト。</summary>
/// <param name="Input">喋らせる本文。</param>
/// <param name="VoiceSelector">利用者が明示した声。null は「指定なし」。</param>
/// <param name="ModelSelector">声の指定に使えるかもしれない <c>model</c>。外れてもエラーにしない。</param>
/// <param name="Speed">0.25〜4.0。Windows 側の有効域へのクランプは <c>Speech/</c> が行う。</param>
public sealed record ValidatedSpeechRequest(
    string Input,
    string? VoiceSelector,
    string? ModelSelector,
    double Speed);

/// <summary>リクエストの検証。純粋な処理なので、そのままテストできる。</summary>
public static class SpeechRequestValidator
{
    public const double MinSpeed = 0.25;
    public const double MaxSpeed = 4.0;
    public const double DefaultSpeed = 1.0;

    public const string DefaultResponseFormat = "wav";

    /// <summary>
    /// 「参照音声を使うので声の指定は無い」という意味で送られてくる値。
    /// <b>これを弾くと全リクエストが落ちる</b>（<c>docs/02</c> §3.3）。
    /// </summary>
    public const string NoVoiceSentinel = "none";

    public static ValidatedSpeechRequest Validate(SpeechRequest? request)
    {
        if (request is null)
        {
            throw ContractException.BadRequest(
                ErrorCodes.InvalidJson, "リクエスト本文がありません。", "JSON のオブジェクトを送ってください。");
        }

        var input = request.Input ?? string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            throw ContractException.BadRequest(
                ErrorCodes.EmptyInput, "テキストが空です。", "input に喋らせる本文を入れてください。");
        }

        var format = string.IsNullOrWhiteSpace(request.ResponseFormat)
            ? DefaultResponseFormat
            : request.ResponseFormat.Trim();

        if (!string.Equals(format, DefaultResponseFormat, StringComparison.OrdinalIgnoreCase))
        {
            throw ContractException.BadRequest(
                ErrorCodes.UnsupportedFormat,
                $"{format} は未対応です。",
                "このサーバは wav のみを返します。他の形式への変換は呼び出す側で行ってください。");
        }

        var speed = request.Speed ?? DefaultSpeed;
        if (!(speed >= MinSpeed && speed <= MaxSpeed))
        {
            throw ContractException.BadRequest(
                ErrorCodes.InvalidSpeed,
                $"speed は {MinSpeed} 〜 {MaxSpeed} の範囲です。",
                $"speed={speed}");
        }

        // stream_format は読むだけで検証しない。sse を要求されてもバイナリを返す（docs/02 §4.2）
        return new ValidatedSpeechRequest(
            Input: input,
            VoiceSelector: NormalizeVoiceSelector(request.Voice),
            ModelSelector: Blank(request.Model),
            Speed: speed);
    }

    /// <summary><c>none</c> と空文字を「指定なし」に潰す。</summary>
    public static string? NormalizeVoiceSelector(string? voice)
    {
        var trimmed = Blank(voice);
        return string.Equals(trimmed, NoVoiceSentinel, StringComparison.OrdinalIgnoreCase) ? null : trimmed;
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
