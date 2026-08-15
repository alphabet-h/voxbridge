namespace OpenAiWindowsTts.Contract;

/// <summary>OpenAI 互換のエラー本体。</summary>
public sealed record ErrorBody(string Message, string Type, string Code, string? Param);

/// <summary>
/// エラーのレスポンス body。
///
/// <c>error</c> は OpenAI 互換のクライアント向け、<c>detail</c> は FastAPI 風のクライアント向け。
/// どちらのパーサでも読めるよう両方を出す。
///
/// <b>参照音声のパスを message にも detail にも絶対に入れないこと。</b>
/// 「本文に ref_wav のパスが出た＝参照音声の失敗」と判定するクライアントがあり、
/// 無関係なエラーが参照音声の問題に化ける（<c>docs/02</c> §6）。
/// </summary>
public sealed record ErrorResponse(ErrorBody Error, string? Detail)
{
    public const string InvalidRequestType = "invalid_request_error";
    public const string ServerErrorType = "server_error";

    public static ErrorResponse Create(
        string code,
        string message,
        string? detail = null,
        string type = InvalidRequestType) =>
        new(new ErrorBody(message, type, code, null), detail);
}
