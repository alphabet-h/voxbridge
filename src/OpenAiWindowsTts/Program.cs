using Microsoft.Extensions.Logging.Console;
using OpenAiWindowsTts.Audio;
using OpenAiWindowsTts.Contract;
using OpenAiWindowsTts.Hosting;
using OpenAiWindowsTts.Speech;

// ============================================================
// 1. 起動オプション
// ============================================================

var parsed = CommandLineOptions.Parse(args);
if (parsed.Error is not null)
{
    Console.Error.WriteLine(parsed.Error);
    Console.Error.WriteLine("使い方は --help を参照してください。");
    return 2;
}

var options = parsed.Options;

if (options.ShowHelp)
{
    Console.Out.WriteLine(CommandLineOptions.Help);
    return 0;
}

// ============================================================
// 2. 声の解決
//
//    ここで一度 WinRT を触っておく。声が引けない PC では、
//    リクエストを受けてから初めて失敗するより、起動時に落ちたほうが原因が分かる。
// ============================================================

IReadOnlyList<InstalledVoice> installedVoices;
try
{
    installedVoices = VoiceCatalog.Installed();
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Windows の音声合成を初期化できませんでした: {exception.Message}");
    return 3;
}

if (options.ListVoices)
{
    foreach (var voice in installedVoices)
    {
        Console.Out.WriteLine($"{voice.Id,-18}{voice.DisplayName}  ({voice.Language}, {voice.Gender})");
    }

    return 0;
}

if (installedVoices.Count == 0)
{
    Console.Error.WriteLine("この PC には音声合成の声が 1 つも入っていません。");
    Console.Error.WriteLine("設定 > 時刻と言語 > 音声認識 から音声パッケージを追加してください。");
    return 3;
}

var defaultVoice = VoiceCatalog.Resolve(installedVoices, options.Voice);
if (defaultVoice is null)
{
    Console.Error.WriteLine($"--voice '{options.Voice}' に一致する声がありません。");
    Console.Error.WriteLine($"使える id: {string.Join(", ", installedVoices.Select(voice => voice.Id))}");
    return 2;
}

// ============================================================
// 3. サーバの組み立て
// ============================================================

var builder = WebApplication.CreateBuilder();

// ログはすべて stderr へ出す。stdout の 1 行目を機械可読のポート通知に使うため、
// 「Now listening on: ...」のような行が先に出ると困る。
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Services.Configure<ConsoleLoggerOptions>(
    console => console.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

builder.WebHost.UseUrls($"http://{options.Host}:{options.Port}");

builder.Services.ConfigureHttpJsonOptions(json => ContractJson.Apply(json.SerializerOptions));

var app = builder.Build();

app.MapGet("/", () => Results.Text(CommandLineOptions.Help, "text/plain; charset=utf-8"));

app.MapGet("/health", () => Results.Ok(HealthResponse.Create(
    voiceDisplayName: defaultVoice.DisplayName,
    maxConcurrentSynthesis: options.Concurrency,
    running: 0,
    queued: 0)));

// 未実装のパスも契約どおりの形で返す。HTML のエラーページを返すと、
// JSON を前提にしたクライアントのパーサが本文ごと画面に出す。
app.MapFallback((HttpContext http) => Results.Json(
    ErrorResponse.Create(
        ErrorCodes.NotFound,
        $"{http.Request.Method} {http.Request.Path} は未実装です",
        "実装済みのパスは GET /health だけです。"),
    statusCode: StatusCodes.Status404NotFound));

// ============================================================
// 4. 起動
// ============================================================

await app.StartAsync();

var boundPort = ResolveBoundPort(app.Urls, options.Port);

// 1 行目は機械可読。--port 0 のとき、起動した側がここから実ポートを読む
Console.Out.WriteLine($"OPENAI_WINDOWS_TTS_PORT={boundPort}");
Console.Out.WriteLine($"openai-windows-tts  http://{options.Host}:{boundPort}");
Console.Out.WriteLine(
    $"  voice={defaultVoice.DisplayName} ({defaultVoice.Language})" +
    $"  {CanonicalFormat.SampleRate}Hz/{CanonicalFormat.BitsPerSample}bit/{CanonicalFormat.Channels}ch" +
    $"  concurrency={options.Concurrency}");
Console.Out.WriteLine($"  この PC の声: {string.Join(", ", installedVoices.Select(voice => voice.Id))}");
Console.Out.Flush();

await app.WaitForShutdownAsync();
return 0;

static int ResolveBoundPort(IEnumerable<string> urls, int requestedPort)
{
    foreach (var url in urls)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var parsedUrl) && parsedUrl.Port > 0)
        {
            return parsedUrl.Port;
        }
    }

    return requestedPort;
}
