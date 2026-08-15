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
        var voice = VoiceCatalog.Resolve(Sample, selector);

        Assert.NotNull(voice);
        Assert.Equal("ichiro", voice.Id);
    }

    [Fact]
    public void 知らない声は_null_を返す()
    {
        // 呼び出し側が 400 / VOICE_NOT_FOUND にする。黙って別の声で喋らせない
        Assert.Null(VoiceCatalog.Resolve(Sample, "zzz"));
    }

    [Fact]
    public void 声が_1_つも無ければ既定も_null()
    {
        Assert.Null(VoiceCatalog.Default([]));
    }
}
