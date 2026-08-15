namespace VoxBridge.Contract;

/// <summary>
/// 契約どおりのエラーを返して終わる。<c>Hosting/ContractErrorMiddleware</c> が
/// <see cref="ErrorResponse"/> に変換して書き出す。
///
/// 例外にしているのは、検証をエンドポイントの外（純粋な関数）へ出しても
/// 呼び出し側に戻り値の分岐を強いないため。分岐が増えると、
/// 「検証は書いたが返し忘れた」経路が生まれる。
/// </summary>
public sealed class ContractException(int statusCode, string code, string message, string? detail = null)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public string Code { get; } = code;

    public string? Detail { get; } = detail;

    public ErrorResponse ToResponse() =>
        ErrorResponse.Create(
            Code,
            Message,
            Detail,
            StatusCode >= 500 ? ErrorResponse.ServerErrorType : ErrorResponse.InvalidRequestType);

    public static ContractException BadRequest(string code, string message, string? detail = null) =>
        new(400, code, message, detail);
}
