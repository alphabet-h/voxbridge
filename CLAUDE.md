# openai-windows-tts

Windows に最初から入っている音声を、OpenAI 互換の HTTP API で喋らせる常駐サーバ。C# / .NET 9。

**設計の正は `docs/` にある。迷ったら実装ではなくドキュメントを読む。**

| 知りたいこと | 読むファイル |
|---|---|
| 何のためのサーバか。やらないと決めたこと | [docs/01-overview.md](docs/01-overview.md) |
| **リクエスト・レスポンス・エラーの形** | [docs/02-http-contract.md](docs/02-http-contract.md) |
| Windows 側の実測値と制約（16 kHz 固定・46 バイトヘッダ・話速の有効域） | [docs/03-windows-speech.md](docs/03-windows-speech.md) |
| **いまどこまで出来ていて、次に何をするか** | [docs/08-next-steps.md](docs/08-next-steps.md) |
| 触って気づいたことの受け皿 | [docs/09-feedback.md](docs/09-feedback.md) |

設計を変えるときは `docs/01`〜`03` を直す。作業ログと申し送りは `docs/08`。

## 言語

**ドキュメント・コードコメント・コミットメッセージは日本語で書く。** 識別子は英語。

## コマンド

| | |
|---|---|
| `dotnet build` | ビルド。**警告 0 が通過条件**（`TreatWarningsAsErrors`） |
| `dotnet test` | テスト |
| `node scripts/check-contract.mjs` | `docs/02` §6.2 の表 ↔ `Contract/ErrorCodes.cs` の照合 |
| `powershell -NoProfile -File scripts\check.ps1` | BOM → ビルド → 整形 → テスト → 契約照合。**コミット前に必ず通す** |
| `powershell -NoProfile -File scripts\check.ps1 -Fix` | 整形の差分をその場で直す |
| `dotnet run --project src/OpenAiWindowsTts -- --port 8288` | 起動 |
| `dotnet run --project src/OpenAiWindowsTts -- --list-voices` | この PC の声を見る |
| `node scripts/smoke.mjs` | サーバを起動して契約適合を叩く |
| `dotnet publish src/OpenAiWindowsTts -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true` | 配布用の単一 exe |

落とし穴:

- **`.ps1` は UTF-8 の BOM 付きで保存する。** Windows PowerShell 5.1 は BOM 無しの `.ps1` を
  CP932 として読むため、**日本語の文字列リテラルがパース時点で壊れる**。
  実際にフックが返すメッセージが丸ごと化けた。`check.ps1` が BOM の有無を検査している。
  **`.ps1` を書き換えたら BOM が残っているか確かめること**（多くのツールは BOM を落とす）
- **サーバの stdout は UTF-8。** `Get-Content` は既定で CP932 として読むので日本語が化ける。
  **`-Encoding UTF8` を付ける。** 出力側は壊れていない
- **PowerShell から native コマンドの出力を `2>&1` で受けない。**
  stderr が `ErrorRecord` に包まれ、`$ErrorActionPreference = 'Stop'` だとそこで即死する。
  `node` は stderr に書くので、これを踏むとフックが exit 2 に到達しない。
  `Start-Process` でファイルへ分けて受ける（`.claude/hooks/cs-post-edit.ps1` が実例）
- **stdout の 1 行目は `OPENAI_WINDOWS_TTS_PORT=<n>`。** ログを stdout に戻すと
  `scripts/smoke.mjs` が実ポートを読めなくなる。ログは全部 stderr へ出している
- `--port 0` を渡すと OS が空きポートを割り当てる。テストと CI はこれを使う
- **`dotnet run` は子プロセスを挟む。** 起動したサーバを確実に止めたいときは
  `bin\...\openai-windows-tts.exe` を直接叩く
- `PublishSingleFile` などの publish 用プロパティを `.csproj` に書かないこと。
  書くと `dotnet build` と `dotnet test` まで publish 都合の挙動になる

## 層と守ること

```
Program.cs                起動オプションの解釈 → 配線 → ルーティング
  ├─ Hosting/             起動オプション。ASP.NET Core の設定機構は使わない
  ├─ Contract/            HTTP に出入りする JSON の形。DTO だけ
  ├─ Speech/              WinRT の音声合成。★ここだけが WinRT を知る
  └─ Audio/               WAV とリサンプル。★純粋なコードだけ
```

1. **WinRT に触るのは `Speech/` だけ。**
   `Windows.*` の型が他所へ漏れると、テストが書けなくなり、実装のどこが Windows 依存なのかも
   追えなくなる。漏れたことに気づくのは、たいてい別 PC で動かないと報告が来たとき。

2. **`Audio/` は純粋なコードのみ。** WinRT も ASP.NET も参照しない。
   リサンプラの品質は「信号を通して測る」以外に検証しようがなく、依存が混ざると測れない。

3. **HTTP の形を知るのは `Contract/` の DTO だけ。**
   `Speech/` が `HttpContext` を知り始めた時点で、契約とエンジンが癒着して片方だけ直せなくなる。

4. **エラーコードの唯一の出典は `Contract/ErrorCodes.cs`。** 文字列を直書きしない。
   `docs/02` §6.2 の表と `scripts/check-contract.mjs` が照合しており、**片方だけ直すと検査が落ちる**。

5. **`48000 / 1ch / 16bit` の唯一の出典は `Audio/CanonicalFormat`。** 数値を直書きしない。
   Windows が返すのは 16 kHz なので、数字が散らばると「どちらの 16 か」が分からなくなる。

6. **対応していない入力は黙って無視する。ただし `/health` の `model` で必ず名乗る。**
   エラーにすると、利用者が参照音声を 1 つ設定しただけで生成が全件失敗する。
   名乗らないと、利用者は効かない設定をいじり続ける。**両方やること**（[docs/01](docs/01-overview.md) §3）。

7. **`ref_wav` のパス文字列を、エラー本文にもログにも出さない。**
   本文にパスが出たことを根拠に「参照音声の失敗」と判定するクライアントがある。
   無関係なエラーが参照音声の問題に化ける（[docs/02](docs/02-http-contract.md) §6.3）。

8. **`voice` に `none` が来てもエラーにしない。** 「指定なし」の意味で送ってくる実装がある。
   ここで弾くと全リクエストが落ちる。逆に、**指定された声が見つからないときは黙って
   既定に落とさず 400 を返す**（[docs/02](docs/02-http-contract.md) §3.3）。

## いまやらないこと

`docs/01` §5 に一覧がある。特に:

- **SSE**（合成が速すぎて刻む意味が無い）
- **mp3 / flac**（符号化器の依存を増やす）
- **CORS ヘッダ**（ローカルサーバに `*` を付けると任意の Web ページから叩ける）
- **`PublishTrimmed`**（CsWinRT がリフレクションで静かに壊れる）
- **設定ファイル**（起動オプションで足りている）
- ESLint / Prettier に相当する静的解析。**`dotnet format whitespace` とコンパイラ警告で足りている**

### 過剰設計のシグナル

どれかが出たら、機能を足す前に設計を見直す。

- `Speech/` の外に WinRT の型が漏れた
- `Speech/` が `HttpContext` を知っている
- `Contract/` にフィールドを足そうとしている
- 「エンジンの切り替え」を抽象化したくなった（喋らせるのは Windows の声だけ）
- 検査を通すために警告を抑制した
- 設定ファイルを読みたくなった

## 検証

コミット前:

1. `powershell -File scripts\check.ps1` — ビルド・テスト・契約の照合
2. `node scripts/smoke.mjs` — 実際にサーバを起動して契約どおりの応答が返るか
3. 音に関わる変更をしたときは、**実際に聞く**。WAV の数値が正しくても音が壊れていることがある

`/smoke` スキルと `contract-reviewer` / `winrt-interop-reviewer` サブエージェントがある。

## コミット規約

Conventional Commits の type ＋ **日本語の常体**。

```
feat: 未知の voice を 400 で返す（docs/02 §3.3）
fix: data チャンクを 44 バイト決め打ちで切っていた（docs/03 §3）
docs: 3 倍アップサンプラの方式を確定する
```

- type は `feat` / `fix` / `docs` / `test` / `refactor` / `build` / `chore`
- scope は使わない。代わりに**末尾の丸括弧**で docs の節番号や課題 ID を指す
- 命令形ではなく常体の平叙文
- **履歴は書き換えない。** `docs/08` がコミットを指すようになると、書き換えた瞬間に申し送りが嘘になる
