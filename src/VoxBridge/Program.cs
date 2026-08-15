using Microsoft.Extensions.Logging.Console;
using VoxBridge.Audio;
using VoxBridge.Contract;
using VoxBridge.Hosting;
using VoxBridge.Speech;

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

VoiceCatalog catalog;
try
{
    catalog = VoiceCatalog.Load();
}
catch (Exception exception)
{
    Console.Error.WriteLine($"Windows の音声合成を初期化できませんでした: {exception.Message}");
    return 3;
}

if (options.ListVoices)
{
    foreach (var voice in catalog.Voices)
    {
        Console.Out.WriteLine($"{voice.Id,-18}{voice.DisplayName}  ({voice.Language}, {voice.Gender})");
    }

    return 0;
}

if (catalog.Voices.Count == 0)
{
    Console.Error.WriteLine("この PC には音声合成の声が 1 つも入っていません。");
    Console.Error.WriteLine("設定 > 時刻と言語 > 音声認識 から音声パッケージを追加してください。");
    return 3;
}

var defaultVoice = catalog.Resolve(options.Voice);
if (defaultVoice is null)
{
    Console.Error.WriteLine($"--voice '{options.Voice}' に一致する声がありません。");
    Console.Error.WriteLine($"使える id: {string.Join(", ", catalog.Voices.Select(voice => voice.Id))}");
    return 2;
}

// ここで 1 回だけ捨て合成をする。
// プロセス最初の 1 回は、同じ入力・同じ声でも 2 回目以降と違う音が返る
// （長さは同じなので自動テストでは捕まらない）。利用者の最初のリクエストを
// その 1 回目にしないため。失敗しても起動は続ける — 合成できない PC でも
// /health と --list-voices は答えられたほうがいい。
var engine = new SpeechEngine(catalog);
try
{
    await engine.WarmUpAsync(defaultVoice, CancellationToken.None);
}
catch (Exception exception)
{
    Console.Error.WriteLine($"警告: 起動時のウォームアップ合成に失敗しました: {exception.Message}");
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

if (options.Verbose)
{
    // 自分のカテゴリだけ上げる。全体を Information にすると
    // ASP.NET Core のリクエストログで溢れて、肝心の 1 行が埋もれる
    builder.Logging.AddFilter(Endpoints.SynthesisLogCategory, LogLevel.Information);
    builder.Logging.AddFilter(ContractErrorMiddleware.LogCategory, LogLevel.Debug);
}

builder.WebHost.UseUrls($"http://{options.Host}:{options.Port}");
builder.WebHost.ConfigureKestrel(kestrel =>
{
    // 既定の 30 MB は、テキストを送るだけの API には広すぎる（docs/02 §3）
    kestrel.Limits.MaxRequestBodySize = ServerSettings.MaxRequestBodyBytes;
});

builder.Services.ConfigureHttpJsonOptions(json => ContractJson.Apply(json.SerializerOptions));

var settings = new ServerSettings(
    DefaultVoice: defaultVoice,
    MaxConcurrentSynthesis: options.Concurrency,
    QueueTimeout: options.QueueTimeout,
    Verbose: options.Verbose);

builder.Services.AddSingleton(catalog);
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(engine);
builder.Services.AddSingleton(new SynthesisGate(options.Concurrency, options.QueueTimeout));

var app = builder.Build();

app.UseContractErrors();
Endpoints.MapAll(app, CommandLineOptions.Help);

// ============================================================
// 4. 起動
// ============================================================

await app.StartAsync();

var boundPort = ResolveBoundPort(app.Urls, options.Port);

// 1 行目は機械可読。--port 0 のとき、起動した側がここから実ポートを読む
Console.Out.WriteLine($"VOXBRIDGE_PORT={boundPort}");
Console.Out.WriteLine($"voxbridge  http://{options.Host}:{boundPort}");
Console.Out.WriteLine(
    $"  voice={defaultVoice.DisplayName} ({defaultVoice.Language})" +
    $"  {CanonicalFormat.SampleRate}Hz/{CanonicalFormat.BitsPerSample}bit/{CanonicalFormat.Channels}ch" +
    $"  concurrency={options.Concurrency}");
Console.Out.WriteLine($"  この PC の声: {string.Join(", ", catalog.Voices.Select(voice => voice.Id))}");
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
