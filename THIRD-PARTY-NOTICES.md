# サードパーティのライセンス表示

`voxbridge` 自身は [MIT ライセンス](LICENSE)です。
ただし**配布用の単一 exe には Microsoft のコンポーネントが同梱されます**。
それぞれの条件は以下のとおりです。

> 調査日 **2026-08-15**。リンク先が改訂されることがあるので、配布の前に確認し直してください。

---

## .NET ランタイム / ASP.NET Core

- `Microsoft.NETCore.App.Runtime.win-x64`
- `Microsoft.AspNetCore.App.Runtime.win-x64`

**MIT License** — © .NET Foundation and Contributors

- https://licenses.nuget.org/MIT
- https://github.com/dotnet/runtime/blob/main/LICENSE.TXT

自己完結（`--self-contained true`）で publish するため、これらは単一 exe に含まれます。
MIT なので再頒布に制限はありません。

---

## Windows SDK の .NET projection

- `Microsoft.Windows.SDK.NET.dll`
- `WinRT.Runtime.dll`

いずれも [`Microsoft.Windows.SDK.NET.Ref`](https://www.nuget.org/packages/Microsoft.Windows.SDK.NET.Ref) に含まれるもので、
**MIT ではありません。** 適用されるのは **Microsoft Windows SDK のライセンス条項**です。

- ライセンス本文: https://aka.ms/WinSDKLicenseURL
  （*MICROSOFT SOFTWARE LICENSE TERMS — MICROSOFT WINDOWS SOFTWARE DEVELOPMENT KIT (SDK) FOR WINDOWS 10*）
- 再頒布可能ファイルの一覧: https://learn.microsoft.com/en-us/legal/windows-sdk/redist

### 再頒布は明示的に認められています

上記の再頒布一覧に専用の節があり、この 2 つのファイルが名指しで挙げられています。

> **Microsoft.Windows.SDK.NET.Ref**
> The files included in the Microsoft.Windows.SDK.NET.Ref Nuget package
> may be distributed unmodified as a NuGet package, or as part of your program
> in order to enable your application to call WinRT Apis
> - ./lib/net8.0/Microsoft.Windows.SDK.NET.dll
> - ./lib/net8.0//WinRT.Runtime.dll

### 付いてくる義務（SDK ライセンス条項 §2.a.ii）

**バイナリを配る場合にだけ効きます。** ソースだけを配るなら関係ありません。

| | このプロジェクトでの対応 |
|---|---|
| 実質的な主機能を足していること | 該当（HTTP サーバ・リサンプル・契約の実装） |
| 受け取る側に、同等以上に保護する条件へ同意させること | 本ファイルと `README.md` で条項を参照させている |
| **自分の著作権表示を出すこと** | `LICENSE` と exe のバージョン情報（`Directory.Build.props` の `Copyright`） |
| ファイルを改変しないこと | 未改変のまま同梱している |
| Microsoft を免責すること | SDK 条項 §2.a.ii のとおり |
| **Microsoft の商標を製品名に使わない・推奨を示唆しない** | 名前を `voxbridge` にしてある（`Windows` を含まない）。README では「Windows の音声合成を使う」という**参照的な記述**にとどめている |

---

## 生成された音声について

**Microsoft は、Windows 内蔵の音声合成が出力した音声データの利用範囲について、
明示的な条項を置いていません。** 2026-08-15 時点で以下を確認した結果です。

| 確認した文書 | 音声合成の出力に関する記述 |
|---|---|
| Windows 11 使用許諾条件（OEM 版） | **無し** |
| Microsoft Windows SDK ライセンス条項 | **無し**（`speech` の言及は音声認識の 1 箇所のみ） |

「無料の Microsoft 音声なら制限なし」という説をインターネット上でよく見かけますが、
出所をたどると **Microsoft の社員ではないコミュニティ回答者**が、
Office のクリップアート EULA から類推して述べたものでした。一次資料ではありません。

> **本プロジェクトの MIT ライセンスは、本プロジェクトのコードにのみ適用されます。
> 生成された音声データには及びません。** 商用利用など、用途によっては
> ご自身で Microsoft に確認してください。

---

## 開発時のみ使うもの（配布物には含まれません）

- `xunit` / `xunit.runner.visualstudio` — Apache-2.0
- `Microsoft.NET.Test.Sdk` — MIT

テストプロジェクト専用で、`dotnet publish` の出力には入りません。
