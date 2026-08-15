using System.Diagnostics;
using System.Text.Json;
using OpenAiWindowsTts.Audio;
using OpenAiWindowsTts.Contract;
using OpenAiWindowsTts.Speech;

namespace OpenAiWindowsTts.Hosting;

/// <summary>ルーティング。HTTP を知るのはここと <c>Contract/</c> の DTO だけ。</summary>
public static class Endpoints
{
    /// <summary>合成 1 件につき 1 行のログを出すカテゴリ。<c>--verbose</c> がここだけを上げる。</summary>
    public const string SynthesisLogCategory = "synthesis";

    public static void MapAll(WebApplication app, string helpText)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(SynthesisLogCategory);

        app.MapGet("/", () => Results.Text(helpText, "text/plain; charset=utf-8"));

        app.MapGet("/health", (SynthesisGate gate, ServerSettings settings) => Results.Ok(
            HealthResponse.Create(
                voiceDisplayName: settings.DefaultVoice.DisplayName,
                maxConcurrentSynthesis: gate.MaxConcurrent,
                running: gate.Running,
                queued: gate.Queued)));

        // /health を持たないサーバを前提に、疎通確認だけをここでやるクライアントがある。
        // 声ごとのエントリも並べるのは、model の選択肢をここから作るクライアントで
        // そのまま声を選べるようにするため（docs/02 §8）
        app.MapGet("/v1/models", (VoiceCatalog catalog) => Results.Ok(
            ListResponse<ModelEntry>.Of(
                [
                    ListResponses.Model(ListResponses.GenericModelId),
                    .. catalog.Voices.Select(voice => ListResponses.Model(voice.Id)),
                ])));

        app.MapGet("/v1/audio/voices", (VoiceCatalog catalog) => Results.Ok(
            ListResponse<VoiceEntry>.Of(
                [.. catalog.Voices.Select(voice =>
                    ListResponses.Voice(voice.Id, voice.DisplayName, voice.Language, voice.Gender))])));

        app.MapPost(
            "/v1/audio/speech",
            (HttpContext http,
             VoiceCatalog catalog,
             SpeechEngine engine,
             SynthesisGate gate,
             ServerSettings settings) => SpeakAsync(http, catalog, engine, gate, settings, logger));

        // 未実装のパスも契約どおりの形で返す。HTML のエラーページを返すと、
        // JSON を前提にしたクライアントのパーサが本文ごと画面に出す。
        //
        // パターンを明示するのが要点。引数なしの MapFallback は `{*path:nonfile}` を使うため、
        // **最後のセグメントにドットを含むパスに一致しない**（/openapi.json や
        // /v1/audio/speech.wav が、本文 0 バイト・Content-Type なしの 404 になる）。
        // FastAPI 系を想定して /openapi.json を叩くクライアントは実在する。
        app.MapFallback("{*path}", (HttpContext http) => Results.Json(
            ErrorResponse.Create(
                ErrorCodes.NotFound,
                $"{http.Request.Method} {http.Request.Path} は未実装です",
                "GET /health, GET /v1/models, GET /v1/audio/voices, POST /v1/audio/speech が使えます。"),
            statusCode: StatusCodes.Status404NotFound));
    }

    /// <summary>
    /// 合成して WAV を返す。<b>レスポンスは自分で書く</b>（<c>IResult</c> を返さない）。
    /// 実行枠を握ったまま本文を流し切る必要があり、<c>IResult</c> に渡すと
    /// このメソッドを抜けた時点で枠が解放されてしまう。
    /// </summary>
    private static async Task SpeakAsync(
        HttpContext http,
        VoiceCatalog catalog,
        SpeechEngine engine,
        SynthesisGate gate,
        ServerSettings settings,
        ILogger logger)
    {
        var request = await ReadRequestAsync(http).ConfigureAwait(false);
        var validated = SpeechRequestValidator.Validate(request);
        var voice = ResolveVoice(catalog, settings, validated);

        var chunks = SentenceSplitter.Split(validated.Input);
        if (chunks.Count == 0)
        {
            throw ContractException.BadRequest(ErrorCodes.EmptyInput, "テキストが空です。");
        }

        var elapsed = Stopwatch.StartNew();
        var aborted = http.RequestAborted;

        using var slot = await gate.EnterAsync(aborted).ConfigureAwait(false);

        // 最初のチャンクだけ先に合成する。
        // **ここまでの失敗は HTTP ステータスとして返せる**。ヘッダを書いた後では返せない。
        var first = Resampler.Upsample3x(
            await engine.SynthesizeAsync(chunks[0], voice, validated.Speed, aborted).ConfigureAwait(false));

        var body = http.Response.Body;
        var single = chunks.Count == 1;
        var totalSamples = first.Length;

        http.Response.ContentType = "audio/wav";

        if (single)
        {
            // 1 チャンクなら長さが確定している。Content-Length を付けたほうがクライアントに親切
            var dataLength = first.Length * CanonicalFormat.BytesPerSample;
            http.Response.ContentLength = WavWriter.HeaderLength + dataLength;
            await body.WriteAsync(WavWriter.CreateHeader(dataLength), aborted).ConfigureAwait(false);
        }
        else
        {
            // 長さが確定していないので size 0 で先にヘッダを返す。
            // 全部合成してから返すと、約 7,000 文字で接続タイムアウトに届く（docs/02 §4.3）
            await body.WriteAsync(WavWriter.CreateHeader(WavWriter.UnknownLength), aborted).ConfigureAwait(false);
        }

        await body.WriteAsync(WavWriter.ToPcmBytes(first), aborted).ConfigureAwait(false);
        await body.FlushAsync(aborted).ConfigureAwait(false);

        for (var index = 1; index < chunks.Count; index++)
        {
            var part = Resampler.Upsample3x(
                await engine.SynthesizeAsync(chunks[index], voice, validated.Speed, aborted).ConfigureAwait(false));

            totalSamples += part.Length;

            await body.WriteAsync(WavWriter.ToPcmBytes(part), aborted).ConfigureAwait(false);
            await body.FlushAsync(aborted).ConfigureAwait(false);
        }

        if (settings.Verbose)
        {
            logger.LogInformation(
                "voice={Voice} chars={Characters} chunks={Chunks} {Elapsed}ms {Seconds:F2}s {Bytes}B",
                voice.Id,
                validated.Input.Length,
                chunks.Count,
                elapsed.ElapsedMilliseconds,
                CanonicalFormat.DurationSeconds(totalSamples * CanonicalFormat.BytesPerSample),
                WavWriter.HeaderLength + (totalSamples * CanonicalFormat.BytesPerSample));
        }
    }

    /// <summary>
    /// 本文を自分で読む。
    ///
    /// <b>Minimal API の自動バインドに任せないこと。</b> JSON が壊れていると、
    /// バインドは例外を投げずに**その場で本文の無い 400 を書いて打ち切る**。
    /// ミドルウェアまで届かないので、契約どおりのエラー body を返せない
    /// （実測: <c>400 0 -</c> = Content-Type すら付かない空応答になる）。
    ///
    /// Content-Type は見ない。付けてこないクライアントも、GET に付けてくるクライアントもいる。
    /// </summary>
    private static async Task<SpeechRequest?> ReadRequestAsync(HttpContext http) =>
        await JsonSerializer
            .DeserializeAsync<SpeechRequest>(http.Request.Body, ContractJson.Options, http.RequestAborted)
            .ConfigureAwait(false);

    /// <summary>
    /// 声を決める。<c>docs/02</c> §3.3 のとおり、上から順に最初に当たったもの。
    ///
    /// <b>voice が指されたのに見つからないときだけ 400 にする。</b>
    /// model が外れてもエラーにしないのは、多くのクライアントが model に
    /// エンジンの名前を入れて送ってくるため。そこで落とすと全リクエストが失敗する。
    /// </summary>
    private static InstalledVoice ResolveVoice(
        VoiceCatalog catalog,
        ServerSettings settings,
        ValidatedSpeechRequest request)
    {
        if (request.VoiceSelector is not null)
        {
            return VoiceCatalog.ResolveIn(catalog.Voices, request.VoiceSelector)
                ?? throw ContractException.BadRequest(
                    ErrorCodes.VoiceNotFound,
                    $"voice '{request.VoiceSelector}' に対応する声がありません。",
                    $"使える id: {string.Join(", ", catalog.Voices.Select(voice => voice.Id))}");
        }

        if (request.ModelSelector is not null)
        {
            var byModel = VoiceCatalog.ResolveIn(catalog.Voices, request.ModelSelector);
            if (byModel is not null)
            {
                return byModel;
            }
        }

        return settings.DefaultVoice;
    }

    private static short[] Concat(List<short[]> parts)
    {
        if (parts.Count == 1)
        {
            return parts[0];
        }

        var total = parts.Sum(part => part.Length);
        var merged = new short[total];

        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(merged, offset);
            offset += part.Length;
        }

        return merged;
    }
}
