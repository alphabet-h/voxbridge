using OpenAiWindowsTts.Speech;

namespace OpenAiWindowsTts.Hosting;

/// <summary>起動時に決まり、動作中は変わらない設定。</summary>
/// <param name="DefaultVoice">voice も model も声を指さないときに使う声。</param>
/// <param name="MaxConcurrentSynthesis">同時に合成する本数。</param>
/// <param name="QueueTimeout">実行枠が空くのを待つ上限。超えたら 503。</param>
/// <param name="Verbose">合成 1 件につき 1 行ログを出すか。</param>
public sealed record ServerSettings(
    InstalledVoice DefaultVoice,
    int MaxConcurrentSynthesis,
    TimeSpan QueueTimeout,
    bool Verbose)
{
    /// <summary>リクエスト本文の上限（<c>docs/02</c> §3）。この数値の唯一の出典。</summary>
    public const long MaxRequestBodyBytes = 4L * 1024 * 1024;
}
