using System.Text;
using Windows.Media.SpeechSynthesis;

namespace OpenAiWindowsTts.Speech;

/// <summary>
/// この PC に入っている声 1 つ。
/// WinRT の <c>VoiceInformation.Id</c> はレジストリのフルパスで API に載せるには長すぎるので、
/// 表示名から短い id を作って持つ（<c>Microsoft Ayumi</c> → <c>ayumi</c>）。
/// </summary>
public sealed record InstalledVoice(string Id, string DisplayName, string Language, string Gender);

/// <summary>
/// この PC の声の一覧。<b>起動時に一度だけ WinRT を読む。</b>
///
/// <b>WinRT に触ってよいのはこの名前空間だけ。</b> <c>VoiceInformation</c> をここから外へ出さない。
/// 外へ漏れると、テストが書けなくなり、実装のどこが Windows 依存なのかも追えなくなる。
/// </summary>
public sealed class VoiceCatalog
{
    private readonly Dictionary<string, VoiceInformation> _systemVoices;

    private VoiceCatalog(
        IReadOnlyList<InstalledVoice> voices,
        InstalledVoice? defaultVoice,
        Dictionary<string, VoiceInformation> systemVoices)
    {
        Voices = voices;
        Default = defaultVoice;
        _systemVoices = systemVoices;
    }

    public IReadOnlyList<InstalledVoice> Voices { get; }

    /// <summary>
    /// Windows の既定の声（表示言語に追随する）。
    ///
    /// ここで「日本語の声を優先」のような細工をしないこと。起動ログに出ている声と
    /// 実際に喋る声が食い違ったときに理由が追えなくなる。選びたいときは <c>--voice</c> で明示する。
    /// </summary>
    public InstalledVoice? Default { get; }

    /// <summary>WinRT を読んで一覧を作る。言語での絞り込みはしない。</summary>
    public static VoiceCatalog Load()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var voices = new List<InstalledVoice>();
        var systemVoices = new Dictionary<string, VoiceInformation>(StringComparer.OrdinalIgnoreCase);

        foreach (var information in SpeechSynthesizer.AllVoices)
        {
            var id = UniqueId(information.DisplayName, used);

            voices.Add(new InstalledVoice(
                Id: id,
                DisplayName: information.DisplayName,
                Language: information.Language,
                Gender: information.Gender.ToString()));

            systemVoices[id] = information;
        }

        InstalledVoice? defaultVoice = null;
        if (voices.Count > 0)
        {
            var systemDefault = SpeechSynthesizer.DefaultVoice?.DisplayName;
            defaultVoice = voices.FirstOrDefault(voice => voice.DisplayName == systemDefault) ?? voices[0];
        }

        return new VoiceCatalog(voices, defaultVoice, systemVoices);
    }

    /// <summary>
    /// 声を引く。<paramref name="selector"/> が空なら既定の声。
    /// 見つからなければ null（呼び出し側が 400 / <c>VOICE_NOT_FOUND</c> にする）。
    /// </summary>
    public InstalledVoice? Resolve(string? selector) =>
        string.IsNullOrWhiteSpace(selector) ? Default : ResolveIn(Voices, selector);

    /// <summary>
    /// 一覧から短い id か表示名で引く純粋な処理。<b>空の selector では既定に落とさない</b> —
    /// 既定へ落とすかどうかは呼び出し側の判断。
    /// </summary>
    public static InstalledVoice? ResolveIn(IReadOnlyList<InstalledVoice> voices, string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return null;
        }

        var wanted = selector.Trim();
        return voices.FirstOrDefault(voice => string.Equals(voice.Id, wanted, StringComparison.OrdinalIgnoreCase))
            ?? voices.FirstOrDefault(voice => string.Equals(voice.DisplayName, wanted, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>WinRT の声。<b>この名前空間の外へ渡さないこと。</b></summary>
    internal VoiceInformation SystemVoice(InstalledVoice voice) =>
        _systemVoices.TryGetValue(voice.Id, out var information)
            ? information
            : throw new InvalidOperationException($"この一覧に属さない声です: {voice.Id}");

    /// <summary>
    /// 表示名から URL に載せられる短い id を作る。
    /// <c>Microsoft Ayumi</c> → <c>ayumi</c>、<c>Microsoft Haruka Desktop</c> → <c>haruka-desktop</c>。
    /// ASCII 英数以外しか含まない表示名（理論上ありうる）は <c>voice</c> に落ちるので、
    /// 呼び出し側で重複を解決する必要がある。
    /// </summary>
    public static string MakeId(string displayName)
    {
        const string vendorPrefix = "Microsoft ";

        // 前後の空白を落としてから接頭辞を見る。順序を逆にすると
        // 先頭に空白のある表示名だけ id に microsoft- が残る
        var trimmed = displayName.Trim();
        var name = trimmed.StartsWith(vendorPrefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[vendorPrefix.Length..]
            : trimmed;

        var builder = new StringBuilder(name.Length);
        foreach (var ch in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                builder.Append(ch);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var id = builder.ToString().Trim('-');
        return id.Length > 0 ? id : "voice";
    }

    private static string UniqueId(string displayName, HashSet<string> used)
    {
        var id = MakeId(displayName);
        if (used.Add(id))
        {
            return id;
        }

        var suffix = 2;
        string candidate;
        do
        {
            candidate = $"{id}-{suffix++}";
        }
        while (!used.Add(candidate));

        return candidate;
    }
}
