using System.Text.Json;
using OpenAiWindowsTts.Contract;

namespace OpenAiWindowsTts.Tests;

public class HealthResponseTests
{
    private static string Serialize() =>
        JsonSerializer.Serialize(
            HealthResponse.Create("Microsoft Ayumi", maxConcurrentSynthesis: 1, running: 0, queued: 0),
            ContractJson.Options);

    [Fact]
    public void キーは_snake_case_で出る()
    {
        var json = Serialize();

        Assert.Contains("\"model_loaded\":", json, StringComparison.Ordinal);
        Assert.Contains("\"sample_rate\":48000", json, StringComparison.Ordinal);
        Assert.Contains("\"bit_depth\":16", json, StringComparison.Ordinal);
        Assert.Contains("\"max_concurrent_synthesis\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"device_name\":", json, StringComparison.Ordinal);
        Assert.Contains("\"vram_total_gb\":0", json, StringComparison.Ordinal);
    }

    [Fact]
    public void 日本語は_エスケープせず_そのまま出す()
    {
        // model 名はクライアントの画面にそのまま出る。\uXXXX に潰れると読めない
        var json = Serialize();

        Assert.Contains("Windows 内蔵音声", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u", json, StringComparison.Ordinal);
    }

    [Fact]
    public void gpu_は省略せず_GPU_を使わないことを名乗る()
    {
        // gpu が無いと、ローカル接続のときに nvidia-smi を叩いて PC の GPU 名を
        // 表示するクライアントがある。使っていない GPU が表示されるのを防ぐ
        var health = HealthResponse.Create("Microsoft Ayumi", 1, 0, 0);

        Assert.NotNull(health.Gpu);
        Assert.NotEmpty(health.Gpu.DeviceName);
        Assert.Equal(0, health.Gpu.VramTotalGb);
    }

    [Fact]
    public void model_名は_非対応の機能を名乗る()
    {
        var name = HealthResponse.ModelName("Microsoft Ichiro");

        Assert.Contains("Microsoft Ichiro", name, StringComparison.Ordinal);
        Assert.Contains("参照音声", name, StringComparison.Ordinal);
        Assert.Contains("caption", name, StringComparison.Ordinal);
        Assert.Contains("seed", name, StringComparison.Ordinal);
    }

    [Fact]
    public void 返せる形式は_wav_だけだと申告する()
    {
        var health = HealthResponse.Create("Microsoft Ayumi", 1, 0, 0);

        Assert.Equal(["wav"], health.ResponseFormats);
        Assert.Equal(["audio"], health.StreamFormats);
    }
}
