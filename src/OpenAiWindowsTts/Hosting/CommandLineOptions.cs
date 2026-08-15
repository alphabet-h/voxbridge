using System.Globalization;

namespace OpenAiWindowsTts.Hosting;

/// <summary>起動オプション。ASP.NET Core の設定機構は使わず、ここで全部解釈する。</summary>
public sealed record CommandLineOptions
{
    /// <summary>
    /// 8088 / 8188 を使う TTS サーバが世の中にあるので、ぶつからない番号にしてある。
    /// 同じ PC で複数のエンジンを同時に立ち上げて比べられる。
    /// </summary>
    public const int DefaultPort = 8288;

    /// <summary>実行枠が空くのを待つ上限（秒）。超えたら 503。</summary>
    public const int DefaultQueueTimeoutSeconds = 30;

    public string Host { get; init; } = "127.0.0.1";
    public int Port { get; init; } = DefaultPort;
    public string? Voice { get; init; }
    public int Concurrency { get; init; } = 1;
    public int QueueTimeoutSeconds { get; init; } = DefaultQueueTimeoutSeconds;
    public bool Verbose { get; init; }
    public bool ShowHelp { get; init; }
    public bool ListVoices { get; init; }

    public TimeSpan QueueTimeout => TimeSpan.FromSeconds(QueueTimeoutSeconds);

    public const string Help = """
        openai-windows-tts — Windows 内蔵の音声を OpenAI 互換 API で喋らせる常駐サーバ

          POST /v1/audio/speech      OpenAI 互換。WAV バイナリを返す
          GET  /health               稼働状態・声・音声フォーマット
          GET  /v1/models            疎通確認用
          GET  /v1/audio/voices      この PC で使える声の一覧

        オプション:
          --host <addr>          既定 127.0.0.1
          --port <n>             既定 8288。0 を渡すと OS が空きポートを割り当てる
          --voice <id|name>      既定の声。省略すると Windows の既定の声
          --concurrency <n>      同時に受け付けるリクエスト数（既定 1）
                                 合成そのものは常に 1 本ずつ。Windows の合成エンジンは
                                 同時に叩くとプロセスごと落ちるため、そこは変えられない
          --queue-timeout <sec>  実行枠が空くのを待つ上限（既定 30）。超えたら 503
          --verbose              合成 1 件につき 1 行ログを出す（stderr）
          --list-voices          この PC の声を並べて終了する
          -h, --help             このヘルプ

        起動時、stdout の 1 行目に実際の待ち受けポートを OPENAI_WINDOWS_TTS_PORT=<n> の形で出す。
        ログはすべて stderr へ出るので、stdout は機械可読のまま使える。

        声はリクエストごとに voice / model フィールドでも切り替えられる。
        voice が none・空・未指定のときは --voice で決めた既定の声になる。

        参照音声・Voice Design の caption・seed・ステップ数は Windows 内蔵の音声に存在しない。
        送られてきても黙って捨てる（エラーにしない）。/health の model 名でそのことを名乗る。
        """;

    /// <summary>解釈の結果。<see cref="Error"/> が非 null なら <see cref="Options"/> は使わない。</summary>
    public sealed record ParseResult(CommandLineOptions Options, string? Error);

    public static ParseResult Parse(string[] args)
    {
        var host = "127.0.0.1";
        var port = DefaultPort;
        string? voice = null;
        var concurrency = 1;
        var queueTimeoutSeconds = DefaultQueueTimeoutSeconds;
        var verbose = false;
        var showHelp = false;
        var listVoices = false;

        CommandLineOptions Build() => new()
        {
            Host = host,
            Port = port,
            Voice = voice,
            Concurrency = concurrency,
            QueueTimeoutSeconds = queueTimeoutSeconds,
            Verbose = verbose,
            ShowHelp = showHelp,
            ListVoices = listVoices,
        };

        for (var i = 0; i < args.Length; i++)
        {
            var (name, inlineValue) = SplitArgument(args[i]);

            // --key=value と --key value の両方を受ける
            string? TakeValue()
            {
                if (inlineValue is not null)
                {
                    return inlineValue;
                }

                if (i + 1 >= args.Length)
                {
                    return null;
                }

                i++;
                return args[i];
            }

            switch (name)
            {
                case "-h" or "--help":
                    showHelp = true;
                    break;

                case "--list-voices":
                    listVoices = true;
                    break;

                case "--verbose":
                    verbose = true;
                    break;

                case "--host":
                    {
                        var value = TakeValue();
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            return Fail(Build(), "--host に値がありません。");
                        }

                        host = value.Trim();
                        break;
                    }

                case "--port":
                    {
                        var value = TakeValue();
                        if (!TryParseInt(value, 0, 65535, out port))
                        {
                            return Fail(Build(), $"--port の値が不正です: {value ?? "(なし)"}（0〜65535）");
                        }

                        break;
                    }

                case "--voice":
                    {
                        var value = TakeValue();
                        if (string.IsNullOrWhiteSpace(value))
                        {
                            return Fail(Build(), "--voice に値がありません。");
                        }

                        voice = value.Trim();
                        break;
                    }

                case "--concurrency":
                    {
                        var value = TakeValue();
                        if (!TryParseInt(value, 1, 64, out concurrency))
                        {
                            return Fail(Build(), $"--concurrency の値が不正です: {value ?? "(なし)"}（1〜64）");
                        }

                        break;
                    }

                case "--queue-timeout":
                    {
                        var value = TakeValue();
                        if (!TryParseInt(value, 1, 3600, out queueTimeoutSeconds))
                        {
                            return Fail(Build(), $"--queue-timeout の値が不正です: {value ?? "(なし)"}（1〜3600 秒）");
                        }

                        break;
                    }

                default:
                    return Fail(Build(), $"不明なオプションです: {args[i]}");
            }
        }

        return new ParseResult(Build(), null);
    }

    private static ParseResult Fail(CommandLineOptions options, string error) => new(options, error);

    private static (string Name, string? InlineValue) SplitArgument(string argument)
    {
        if (!argument.StartsWith("--", StringComparison.Ordinal))
        {
            return (argument, null);
        }

        var separator = argument.IndexOf('=', StringComparison.Ordinal);
        return separator > 0
            ? (argument[..separator], argument[(separator + 1)..])
            : (argument, null);
    }

    private static bool TryParseInt(string? raw, int min, int max, out int value)
    {
        value = min;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        if (parsed < min || parsed > max)
        {
            return false;
        }

        value = parsed;
        return true;
    }
}
