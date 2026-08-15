using System.Text.Json;
using VoxBridge.Contract;

namespace VoxBridge.Tests;

public class SpeechRequestTests
{
    private static SpeechRequest? Parse(string json) =>
        JsonSerializer.Deserialize<SpeechRequest>(json, ContractJson.Options);

    [Fact]
    public void snake_case_のキーを読む()
    {
        var request = Parse("""
            {"input":"テスト","model":"m","voice":"ayumi","response_format":"wav","speed":1.5,"stream_format":"sse"}
            """);

        Assert.NotNull(request);
        Assert.Equal("テスト", request.Input);
        Assert.Equal("m", request.Model);
        Assert.Equal("ayumi", request.Voice);
        Assert.Equal("wav", request.ResponseFormat);
        Assert.Equal(1.5, request.Speed);
        Assert.Equal("sse", request.StreamFormat);
    }

    [Fact]
    public void voice_はオブジェクトでも読める()
    {
        var request = Parse("""{"input":"テスト","voice":{"id":"haruka","name":"無視される"}}""");

        Assert.Equal("haruka", request?.Voice);
    }

    [Theory]
    [InlineData("""{"input":"テスト","voice":null}""")]
    [InlineData("""{"input":"テスト","voice":123}""")]
    [InlineData("""{"input":"テスト","voice":["a"]}""")]
    [InlineData("""{"input":"テスト","voice":{"name":"id が無い"}}""")]
    public void voice_が想定外の形でも落とさない(string json)
    {
        // 落とすと、そのクライアントからは何も生成できなくなる
        var request = Parse(json);

        Assert.NotNull(request);
        Assert.Null(request.Voice);
    }

    [Fact]
    public void 知らないキーは読まない()
    {
        // 参照音声・caption・seed は「拾って捨てる」のではなく、そもそも拾わない。
        // 400 にすると、利用者が参照音声を 1 つ設定しただけで生成が全件失敗する
        var request = Parse("""
            {
              "input":"テスト",
              "irodori":{"caption":"明るく","seed":1,"num_steps":32,"ref_wav":"C:/no/such.wav"},
              "未知のキー":{"入れ子":[1,2,3]}
            }
            """);

        Assert.NotNull(request);
        Assert.Equal("テスト", request.Input);
    }

    [Fact]
    public void 拡張オブジェクトのキー名が違っても落とさない()
    {
        // 拡張の名前空間を設定で変えられるクライアントがある
        var request = Parse("""{"input":"テスト","別名":{"caption":"x"}}""");

        Assert.Equal("テスト", request?.Input);
    }
}

public class SpeechRequestValidatorTests
{
    private static SpeechRequest Request(
        string? input = "テスト",
        string? voice = null,
        string? model = null,
        string? format = null,
        double? speed = null) =>
        new() { Input = input, Voice = voice, Model = model, ResponseFormat = format, Speed = speed };

    [Fact]
    public void 既定値()
    {
        var validated = SpeechRequestValidator.Validate(Request());

        Assert.Equal("テスト", validated.Input);
        Assert.Null(validated.VoiceSelector);
        Assert.Null(validated.ModelSelector);
        Assert.Equal(1.0, validated.Speed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public void 本文が空なら_EMPTY_INPUT(string? input)
    {
        var error = Assert.Throws<ContractException>(() => SpeechRequestValidator.Validate(Request(input: input)));

        Assert.Equal(ErrorCodes.EmptyInput, error.Code);
        Assert.Equal(400, error.StatusCode);
    }

    [Fact]
    public void 本文が無いリクエストは_INVALID_JSON()
    {
        var error = Assert.Throws<ContractException>(() => SpeechRequestValidator.Validate(null));

        Assert.Equal(ErrorCodes.InvalidJson, error.Code);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("NONE")]
    [InlineData(" none ")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void voice_の_none_と空は指定なしに潰す(string? voice)
    {
        // 「参照音声を使うので声の指定は無い」の意味で none を送る実装がある。
        // ここで弾くと全リクエストが落ちる
        var validated = SpeechRequestValidator.Validate(Request(voice: voice));

        Assert.Null(validated.VoiceSelector);
    }

    [Fact]
    public void voice_が指されていれば残す()
    {
        var validated = SpeechRequestValidator.Validate(Request(voice: " ayumi "));

        Assert.Equal("ayumi", validated.VoiceSelector);
    }

    [Fact]
    public void model_も声の候補として残す()
    {
        var validated = SpeechRequestValidator.Validate(Request(model: "haruka"));

        Assert.Equal("haruka", validated.ModelSelector);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wav")]
    [InlineData("WAV")]
    [InlineData("  wav  ")]
    public void wav_は通す(string? format)
    {
        var validated = SpeechRequestValidator.Validate(Request(format: format));

        Assert.Equal("テスト", validated.Input);
    }

    [Theory]
    [InlineData("mp3")]
    [InlineData("flac")]
    [InlineData("opus")]
    [InlineData("pcm")]
    public void wav_以外は_UNSUPPORTED_FORMAT(string format)
    {
        var error = Assert.Throws<ContractException>(
            () => SpeechRequestValidator.Validate(Request(format: format)));

        Assert.Equal(ErrorCodes.UnsupportedFormat, error.Code);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(1.0)]
    [InlineData(4.0)]
    public void speed_の範囲内は通す(double speed)
    {
        Assert.Equal(speed, SpeechRequestValidator.Validate(Request(speed: speed)).Speed);
    }

    [Theory]
    [InlineData(0.24)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4.01)]
    [InlineData(100)]
    [InlineData(double.NaN)]
    public void speed_の範囲外は_INVALID_SPEED(double speed)
    {
        var error = Assert.Throws<ContractException>(() => SpeechRequestValidator.Validate(Request(speed: speed)));

        Assert.Equal(ErrorCodes.InvalidSpeed, error.Code);
    }

    [Fact]
    public void エラー本文に参照音声のパスが混ざらない()
    {
        // 本文にパスが出たことを根拠に「参照音声の失敗」と判定するクライアントがある。
        // そもそも ref_wav を読まないので混ざりようがないが、経路として確認しておく
        var request = new SpeechRequest { Input = "", Voice = "C:/ref/mika.wav" };

        var error = Assert.Throws<ContractException>(() => SpeechRequestValidator.Validate(request));

        Assert.DoesNotContain("mika.wav", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("mika.wav", error.Detail ?? "", StringComparison.Ordinal);
    }
}
