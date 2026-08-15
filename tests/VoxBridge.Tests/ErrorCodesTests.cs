using System.Text.RegularExpressions;
using VoxBridge.Contract;

namespace VoxBridge.Tests;

public class ErrorCodesTests
{
    [Fact]
    public void 一覧は空でなく重複もない()
    {
        Assert.NotEmpty(ErrorCodes.All);
        Assert.Equal(ErrorCodes.All.Count, ErrorCodes.All.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void すべて大文字のスネークケース()
    {
        // scripts/check-contract.mjs が docs/02 の表からこの形で拾う。
        // 小文字やハイフンが混ざると照合がすり抜ける
        foreach (var code in ErrorCodes.All)
        {
            Assert.Matches(new Regex("^[A-Z][A-Z0-9_]*$"), code);
        }
    }

    [Fact]
    public void const_を足したら一覧にも自動で入る()
    {
        Assert.Contains(ErrorCodes.VoiceNotFound, ErrorCodes.All);
        Assert.Contains(ErrorCodes.EngineBusy, ErrorCodes.All);
        Assert.Contains(ErrorCodes.NotFound, ErrorCodes.All);
    }
}
