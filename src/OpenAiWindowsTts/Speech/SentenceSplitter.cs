namespace OpenAiWindowsTts.Speech;

/// <summary>
/// 合成の単位にテキストを分ける。純粋な処理で、WinRT には触らない。
///
/// なぜ分けるのか: 合成と変換で 1.1 ms/文字（実測）。**約 13,000 文字で接続タイムアウト 15 秒に届く。**
/// 全部合成してからヘッダを返す実装は、長めの段落 1 つで構造的に踏む。
/// 最初のチャンクだけ先に合成してヘッダを返し、残りを流しながら合成する。
///
/// なぜ短い入力は分けないのか: チャンクごとに合成を呼ぶと、**境界に無音が積み増される**
/// 可能性がある。必要のない場面でそのリスクを背負わない。
/// </summary>
public static class SentenceSplitter
{
    /// <summary>これ以下なら分割しない。1,000 文字 ≒ 3.8 秒の合成で、タイムアウトには遠い。</summary>
    public const int SingleChunkLimit = 1_000;

    /// <summary>最初のチャンクの上限。ヘッダを返すまでを 1 秒未満に抑えるため小さくする。</summary>
    public const int FirstChunkLimit = 200;

    /// <summary>2 個目以降のチャンクの上限。</summary>
    public const int ChunkLimit = 1_000;

    /// <summary>文の終わり。ここで切れれば継ぎ目が自然になる。</summary>
    private static readonly char[] SentenceEndings = ['。', '！', '？', '!', '?', '.', '\n'];

    /// <summary>文の途中の切れ目。文末が見つからないときの次善。</summary>
    private static readonly char[] PhraseBreaks = ['、', '，', ',', '；', ';', '　', ' ', '\t'];

    public static IReadOnlyList<string> Split(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        if (text.Length <= SingleChunkLimit)
        {
            return [text];
        }

        var chunks = new List<string>();
        var remaining = text.AsMemory();
        var limit = FirstChunkLimit;

        while (remaining.Length > 0)
        {
            var take = TakeLength(remaining.Span, limit);
            chunks.Add(remaining[..take].ToString());
            remaining = remaining[take..];
            limit = ChunkLimit;
        }

        return chunks;
    }

    /// <summary>
    /// 先頭から何文字取るか。文末 → 文中の切れ目 → 上限で強制、の順に探す。
    /// </summary>
    private static int TakeLength(ReadOnlySpan<char> text, int limit)
    {
        if (text.Length <= limit)
        {
            return text.Length;
        }

        var window = text[..limit];

        var sentenceEnd = window.LastIndexOfAny(SentenceEndings);
        if (sentenceEnd >= 0)
        {
            return sentenceEnd + 1;
        }

        var phraseBreak = window.LastIndexOfAny(PhraseBreaks);
        if (phraseBreak >= 0)
        {
            return phraseBreak + 1;
        }

        // 区切りが 1 つも無い長い塊。ここで切るしかない
        return limit;
    }
}
