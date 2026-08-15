using OpenAiWindowsTts.Speech;

namespace OpenAiWindowsTts.Tests;

public class VoiceCatalogTests
{
    private static readonly IReadOnlyList<InstalledVoice> Sample =
    [
        new("ayumi", "Microsoft Ayumi", "ja-JP", "Female"),
        new("haruka", "Microsoft Haruka", "ja-JP", "Female"),
        new("ichiro", "Microsoft Ichiro", "ja-JP", "Male"),
    ];

    [Theory]
    [InlineData("Microsoft Ayumi", "ayumi")]
    [InlineData("Microsoft Haruka Desktop", "haruka-desktop")]
    [InlineData("Zira", "zira")]
    [InlineData("  Microsoft  Zira   Desktop  ", "zira-desktop")]
    [InlineData("Microsoft Server Speech Text to Speech Voice (ja-JP, Ayumi)", "server-speech-text-to-speech-voice-ja-jp-ayumi")]
    public void 表示名から短い_id_を作る(string displayName, string expected)
    {
        Assert.Equal(expected, VoiceCatalog.MakeId(displayName));
    }

    [Fact]
    public void ASCII_英数を含まない表示名は_voice_に落ちる()
    {
        // 呼び出し側が重複を解決する前提。落ちた結果が空文字だと id にできない
        Assert.Equal("voice", VoiceCatalog.MakeId("日本語の声"));
    }

    [Theory]
    [InlineData("ichiro")]
    [InlineData("ICHIRO")]
    [InlineData("Microsoft Ichiro")]
    [InlineData(" ichiro ")]
    public void id_でも表示名でも大文字小文字を問わず引ける(string selector)
    {
        var voice = VoiceCatalog.ResolveIn(Sample, selector);

        Assert.NotNull(voice);
        Assert.Equal("ichiro", voice.Id);
    }

    [Fact]
    public void 知らない声は_null_を返す()
    {
        // 呼び出し側が 400 / VOICE_NOT_FOUND にする。黙って別の声で喋らせない
        Assert.Null(VoiceCatalog.ResolveIn(Sample, "zzz"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 指定が空なら_ResolveIn_は既定に落とさず_null_を返す(string? selector)
    {
        // 「指定なし」と「知らない声」を呼び出し側で区別できるようにしておく。
        // 混ぜると、voice: none を弾くか既定に落とすかの判断ができなくなる
        Assert.Null(VoiceCatalog.ResolveIn(Sample, selector));
    }

    [Fact]
    public void 空の一覧からは何も引けない()
    {
        Assert.Null(VoiceCatalog.ResolveIn([], "ayumi"));
    }

    [Fact]
    public void この_PC_の声を実際に読める()
    {
        // WinRT の projection が動いていることの確認。ここが落ちるなら TFM か SDK の問題
        var catalog = VoiceCatalog.Load();

        Assert.NotEmpty(catalog.Voices);
        Assert.NotNull(catalog.Default);
        Assert.All(catalog.Voices, voice =>
        {
            Assert.NotEmpty(voice.Id);
            Assert.NotEmpty(voice.DisplayName);
            Assert.NotEmpty(voice.Language);
        });

        // id は一意でなければならない（voice で引けなくなる）
        Assert.Equal(catalog.Voices.Count, catalog.Voices.Select(voice => voice.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
