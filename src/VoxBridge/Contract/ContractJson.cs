using System.Text.Encodings.Web;
using System.Text.Json;

namespace VoxBridge.Contract;

/// <summary>
/// HTTP に出す JSON の形の唯一の出典。
///
/// 実行時は <see cref="Apply"/> を ASP.NET Core の設定へ、テストは <see cref="Options"/> を使う。
/// 2 箇所に同じ設定を書くと、テストは通るのに実物のキー名が違う、という壊れ方をする。
/// </summary>
public static class ContractJson
{
    public static void Apply(JsonSerializerOptions options)
    {
        // sample_rate / max_concurrent_synthesis のような snake_case のキーを出す
        options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.PropertyNameCaseInsensitive = true;

        // 日本語を \uXXXX に潰さない。model 名はそのままクライアントの画面に出る。
        // JSON を HTML に直接埋め込む用途は無いので Relaxed で問題ない。
        options.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    }

    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Apply(options);

        // 引数なしの MakeReadOnly() は TypeInfoResolver 未設定だと投げる。
        // true を渡すと既定のリフレクション解決器が入る（このプロジェクトはトリミングしない）
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
