using OpenAiWindowsTts.Hosting;

namespace OpenAiWindowsTts.Tests;

public class CommandLineOptionsTests
{
    [Fact]
    public void 既定値()
    {
        var parsed = CommandLineOptions.Parse([]);

        Assert.Null(parsed.Error);
        Assert.Equal("127.0.0.1", parsed.Options.Host);
        Assert.Equal(8288, parsed.Options.Port);
        Assert.Equal(1, parsed.Options.Concurrency);
        Assert.Null(parsed.Options.Voice);
        Assert.False(parsed.Options.ShowHelp);
        Assert.False(parsed.Options.ListVoices);
    }

    [Fact]
    public void 既定ポートは_他の_TTS_サーバとぶつからない番号()
    {
        // 8088 / 8188 を使うサーバがある。同じ PC で並べて比べられるようにしておく
        Assert.NotEqual(8088, CommandLineOptions.DefaultPort);
        Assert.NotEqual(8188, CommandLineOptions.DefaultPort);
    }

    [Theory]
    [InlineData(new[] { "--port", "0" }, 0)]
    [InlineData(new[] { "--port=0" }, 0)]
    [InlineData(new[] { "--port", "65535" }, 65535)]
    public void ポートは_空白区切りでも等号でも受ける(string[] args, int expected)
    {
        var parsed = CommandLineOptions.Parse(args);

        Assert.Null(parsed.Error);
        Assert.Equal(expected, parsed.Options.Port);
    }

    [Theory]
    [InlineData("--port", "70000")]
    [InlineData("--port", "-1")]
    [InlineData("--port", "abc")]
    [InlineData("--concurrency", "0")]
    [InlineData("--concurrency", "999")]
    public void 範囲外や数字でない値は_エラーにする(string name, string value)
    {
        var parsed = CommandLineOptions.Parse([name, value]);

        Assert.NotNull(parsed.Error);
    }

    [Fact]
    public void 値の無いオプションは_エラーにする()
    {
        Assert.NotNull(CommandLineOptions.Parse(["--voice"]).Error);
        Assert.NotNull(CommandLineOptions.Parse(["--host"]).Error);
    }

    [Fact]
    public void 知らないオプションは_黙って無視せずエラーにする()
    {
        // 起動オプションの打ち間違いを黙って流すと、
        // 「--voice を渡したのに既定の声で喋る」を延々と追うことになる
        var parsed = CommandLineOptions.Parse(["--voise", "ayumi"]);

        Assert.NotNull(parsed.Error);
        Assert.Contains("--voise", parsed.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void 声と同時実行数を受ける()
    {
        var parsed = CommandLineOptions.Parse(["--voice", "haruka", "--concurrency", "4", "--host", "0.0.0.0"]);

        Assert.Null(parsed.Error);
        Assert.Equal("haruka", parsed.Options.Voice);
        Assert.Equal(4, parsed.Options.Concurrency);
        Assert.Equal("0.0.0.0", parsed.Options.Host);
    }

    [Theory]
    [InlineData("-h")]
    [InlineData("--help")]
    public void ヘルプ(string flag)
    {
        var parsed = CommandLineOptions.Parse([flag]);

        Assert.Null(parsed.Error);
        Assert.True(parsed.Options.ShowHelp);
    }

    [Fact]
    public void ヘルプ本文には全オプションが載っている()
    {
        foreach (var option in new[] { "--host", "--port", "--voice", "--concurrency", "--list-voices", "--help" })
        {
            Assert.Contains(option, CommandLineOptions.Help, StringComparison.Ordinal);
        }
    }
}
