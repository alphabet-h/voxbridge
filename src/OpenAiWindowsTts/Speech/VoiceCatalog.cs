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
/// <b>WinRT に触ってよいのはこの名前空間だけ。</b>
/// 他所に散ると、テストが書けなくなり、Windows 以外での検証も永久に不可能になる。
/// </summary>
public static class VoiceCatalog
{
    /// <summary>この PC に入っている声を全部返す。言語での絞り込みはしない。</summary>
    public static IReadOnlyList<InstalledVoice> Installed()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var voices = new List<InstalledVoice>();

        foreach (var info in SpeechSynthesizer.AllVoices)
        {
            voices.Add(new InstalledVoice(
                Id: UniqueId(info.DisplayName, used),
                DisplayName: info.DisplayName,
                Language: info.Language,
                Gender: info.Gender.ToString()));
        }

        return voices;
    }

    /// <summary>
    /// 既定の声。<b>Windows の既定をそのまま使う</b>（表示言語に追随する）。
    /// ここで「日本語の声を優先」のような細工をすると、起動ログに出ている声と
    /// 実際に喋る声が食い違ったときに理由が追えなくなる。選びたいときは <c>--voice</c> で明示する。
    /// </summary>
    public static InstalledVoice? Default(IReadOnlyList<InstalledVoice> voices)
    {
        if (voices.Count == 0)
        {
            return null;
        }

        var systemDefault = SpeechSynthesizer.DefaultVoice?.DisplayName;
        return voices.FirstOrDefault(voice => voice.DisplayName == systemDefault) ?? voices[0];
    }

    /// <summary>
    /// 短い id か表示名で声を引く。<c>selector</c> が空なら既定の声。
    /// 見つからなければ null（呼び出し側が 400 / <c>VOICE_NOT_FOUND</c> にする）。
    /// </summary>
    public static InstalledVoice? Resolve(IReadOnlyList<InstalledVoice> voices, string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
        {
            return Default(voices);
        }

        var wanted = selector.Trim();
        return voices.FirstOrDefault(voice => string.Equals(voice.Id, wanted, StringComparison.OrdinalIgnoreCase))
            ?? voices.FirstOrDefault(voice => string.Equals(voice.DisplayName, wanted, StringComparison.OrdinalIgnoreCase));
    }

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
