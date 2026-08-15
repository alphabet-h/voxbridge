using VoxBridge.Audio;

namespace VoxBridge.Contract;

/// <summary>
/// <c>/health</c> の <c>gpu</c>。
///
/// <b>省略しないこと。</b> GPU の情報が無いと、ローカル接続のときに自前で <c>nvidia-smi</c> を叩いて
/// PC の GPU 名を表示するクライアントがある。このサーバは GPU を使わないのに、
/// 使っているように見えてしまう。<c>vram_total_gb</c> を 0 にしておくと VRAM 欄は空表示になる。
/// </summary>
public sealed record GpuInfo(string DeviceName, double VramUsedGb, double VramTotalGb)
{
    public static GpuInfo None { get; } = new("CPU（GPU 不使用）", 0, 0);
}

/// <summary><c>GET /health</c> のレスポンス。</summary>
public sealed record HealthResponse(
    string Status,
    string Model,
    bool ModelLoaded,
    GpuInfo Gpu,
    int SampleRate,
    int Channels,
    int BitDepth,
    IReadOnlyList<string> ResponseFormats,
    IReadOnlyList<string> StreamFormats,
    int MaxConcurrentSynthesis,
    int Running,
    int Queued)
{
    /// <summary>
    /// 多くのクライアントは <c>/health</c> の <c>model</c> をそのまま画面に出す。
    ///
    /// このサーバが「受け取っても黙って捨てる」機能（参照音声・caption・seed）を
    /// 利用者に伝えられる口はここしかない。<b>必ず名乗らせること</b>（<c>docs/01</c> §3）。
    /// </summary>
    public static string ModelName(string voiceDisplayName) =>
        $"Windows 内蔵音声 / {voiceDisplayName}（参照音声・caption・seed は非対応）";

    public static HealthResponse Create(string voiceDisplayName, int maxConcurrentSynthesis, int running, int queued) =>
        new(
            Status: "ok",
            Model: ModelName(voiceDisplayName),
            ModelLoaded: true,
            Gpu: GpuInfo.None,
            SampleRate: CanonicalFormat.SampleRate,
            Channels: CanonicalFormat.Channels,
            BitDepth: CanonicalFormat.BitsPerSample,
            ResponseFormats: ["wav"],
            StreamFormats: ["audio"],
            MaxConcurrentSynthesis: maxConcurrentSynthesis,
            Running: running,
            Queued: queued);
}
