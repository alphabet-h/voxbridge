using System.Text.Json;
using OpenAiWindowsTts.Contract;
using OpenAiWindowsTts.Speech;

namespace OpenAiWindowsTts.Hosting;

/// <summary>
/// 例外を <c>docs/02</c> §6.1 の形へ揃える。
///
/// Kestrel や ASP.NET Core が素で返す 400 / 413 は本文が空か HTML で、
/// JSON を前提にしたクライアントのパーサがそれを本文ごと画面に出す。
/// </summary>
public static class ContractErrorMiddleware
{
    public const string LogCategory = "contract-error";

    public static IApplicationBuilder UseContractErrors(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            try
            {
                await next(context);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // クライアントが切った。返す相手がもういないので何もしない
            }
            catch (Exception exception)
            {
                var logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger(LogCategory);

                if (context.Response.HasStarted)
                {
                    // ヘッダを返した後に失敗した。ここで正常終了すると、クライアントは
                    // **短いだけの正常な WAV** を受け取り、音が途中で切れたことに誰も気づかない。
                    // 接続を切れば失敗として扱われる（docs/02 §4.2）
                    logger.LogError(exception, "レスポンス送出中に失敗したため接続を切ります。");
                    context.Abort();
                    return;
                }

                var (statusCode, response) = Translate(exception);

                // ContractException は契約で決めた応答であって不具合ではない。
                // 503 のたびにスタックトレースを吐くとログが読めなくなる
                if (exception is ContractException)
                {
                    logger.LogDebug("{Status} {Code}: {Message}", statusCode, response.Error.Code, response.Error.Message);
                }
                else if (statusCode >= 500)
                {
                    logger.LogError(exception, "リクエストの処理に失敗しました。");
                }
                else
                {
                    logger.LogDebug(exception, "{Status} {Code}", statusCode, response.Error.Code);
                }

                context.Response.Clear();
                context.Response.StatusCode = statusCode;

                if (statusCode == StatusCodes.Status503ServiceUnavailable)
                {
                    context.Response.Headers.RetryAfter = "5";
                }

                await context.Response.WriteAsJsonAsync(response, ContractJson.Options, context.RequestAborted);
            }
        });

    private static (int StatusCode, ErrorResponse Response) Translate(Exception exception)
    {
        switch (exception)
        {
            case ContractException contract:
                return (contract.StatusCode, contract.ToResponse());

            case BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge }:
                return (
                    StatusCodes.Status413PayloadTooLarge,
                    ErrorResponse.Create(
                        ErrorCodes.PayloadTooLarge,
                        "リクエスト本文が大きすぎます。",
                        $"本文の上限は {ServerSettings.MaxRequestBodyBytes / (1024 * 1024)} MiB です。" +
                        "テキストを分けて送ってください。"));

            case JsonException json:
                return (
                    StatusCodes.Status400BadRequest,
                    ErrorResponse.Create(
                        ErrorCodes.InvalidJson,
                        "リクエスト JSON を解釈できません。",
                        json.Message));

            case BadHttpRequestException badRequest:
                return (
                    StatusCodes.Status400BadRequest,
                    ErrorResponse.Create(
                        ErrorCodes.InvalidJson,
                        "リクエストを解釈できません。",
                        badRequest.Message));

            case SpeechSynthesisException synthesis:
                return (
                    StatusCodes.Status500InternalServerError,
                    ErrorResponse.Create(
                        ErrorCodes.EngineInternal,
                        "音声を合成できませんでした。",
                        synthesis.Message,
                        ErrorResponse.ServerErrorType));

            default:
                return (
                    StatusCodes.Status500InternalServerError,
                    ErrorResponse.Create(
                        ErrorCodes.EngineInternal,
                        "サーバ内部でエラーが発生しました。",
                        exception.Message,
                        ErrorResponse.ServerErrorType));
        }
    }
}
