using OpenAiWindowsTts.Speech;

namespace OpenAiWindowsTts.Tests;

public class SentenceSplitterTests
{
    [Fact]
    public void 空の入力は空の一覧()
    {
        Assert.Empty(SentenceSplitter.Split(""));
        Assert.Empty(SentenceSplitter.Split("   "));
    }

    [Theory]
    [InlineData("こんにちは。")]
    [InlineData("一文だけ。二文目もある。三文目まである。")]
    public void 短い入力は分割しない(string text)
    {
        // 分割すると境界に無音が積み増される。必要のない場面でそのリスクを背負わない
        var chunks = SentenceSplitter.Split(text);

        Assert.Equal([text], chunks);
    }

    [Fact]
    public void 上限ちょうどまでは分割しない()
    {
        var text = new string('あ', SentenceSplitter.SingleChunkLimit);

        Assert.Single(SentenceSplitter.Split(text));
    }

    [Fact]
    public void 上限を超えたら分割する()
    {
        var text = new string('あ', SentenceSplitter.SingleChunkLimit + 1);

        Assert.True(SentenceSplitter.Split(text).Count > 1);
    }

    [Fact]
    public void 最初のチャンクは小さくする()
    {
        // ヘッダを返すまでの時間を 1 秒未満に抑えるため。
        // 最初が 1,000 文字だと約 3.8 秒かかり、厳しいクライアントの接続タイムアウトに触る
        var text = string.Concat(Enumerable.Repeat("これはテストの文です。", 300));

        var chunks = SentenceSplitter.Split(text);

        Assert.True(chunks[0].Length <= SentenceSplitter.FirstChunkLimit,
            $"最初のチャンクが {chunks[0].Length} 文字あります");
    }

    [Fact]
    public void 二個目以降は上限まで詰める()
    {
        var text = string.Concat(Enumerable.Repeat("これはテストの文です。", 300));

        var chunks = SentenceSplitter.Split(text);

        Assert.All(chunks.Skip(1), chunk =>
            Assert.True(chunk.Length <= SentenceSplitter.ChunkLimit, $"{chunk.Length} 文字あります"));
    }

    [Fact]
    public void 分割しても文字が欠けたり増えたりしない()
    {
        var text = string.Concat(Enumerable.Repeat("これはテストの文です。次の文もあります！さらに問いかけ？", 100));

        var chunks = SentenceSplitter.Split(text);

        Assert.Equal(text, string.Concat(chunks));
    }

    [Theory]
    [InlineData('。')]
    [InlineData('！')]
    [InlineData('？')]
    [InlineData('!')]
    [InlineData('?')]
    [InlineData('.')]
    [InlineData('\n')]
    public void 文末で切る(char ending)
    {
        var sentence = new string('あ', 50) + ending;
        var text = string.Concat(Enumerable.Repeat(sentence, 40));

        var chunks = SentenceSplitter.Split(text);

        Assert.All(chunks.SkipLast(1), chunk => Assert.Equal(ending, chunk[^1]));
    }

    [Fact]
    public void 文末が無ければ読点で切る()
    {
        var text = string.Concat(Enumerable.Repeat(new string('あ', 30) + '、', 100));

        var chunks = SentenceSplitter.Split(text);

        Assert.All(chunks.SkipLast(1), chunk => Assert.Equal('、', chunk[^1]));
    }

    [Fact]
    public void 区切りが一つも無い長い塊も上限で切る()
    {
        // 切れ目が無いからといって、全部を 1 チャンクにするとタイムアウトを踏む
        var text = new string('あ', 5_000);

        var chunks = SentenceSplitter.Split(text);

        Assert.Equal(text, string.Concat(chunks));
        Assert.Equal(SentenceSplitter.FirstChunkLimit, chunks[0].Length);
        Assert.All(chunks, chunk => Assert.True(chunk.Length <= SentenceSplitter.ChunkLimit));
    }

    [Fact]
    public void すべてのチャンクが空でない()
    {
        var text = string.Concat(Enumerable.Repeat("。", 2_000));

        var chunks = SentenceSplitter.Split(text);

        Assert.All(chunks, chunk => Assert.NotEmpty(chunk));
    }
}
