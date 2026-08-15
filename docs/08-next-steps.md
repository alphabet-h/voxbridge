# 08 申し送り

> **最終更新: 2026-08-15**
>
> **いまどこにいるか**: リポジトリの土台と Claude Code の環境を整え、`dotnet build` / `dotnet test` /
> `dotnet run` が通るところまで。サーバは `GET /health` と `GET /` だけ返す。
> WinRT の projection が実際に動くことは `--list-voices` で確認済み（3 声が引ける）。
>
> **次にやること**: §2.1（`Speech/` の合成ラッパ）→ §2.2（`Audio/` の 3 倍アップサンプラ）→
> §2.3（`POST /v1/audio/speech`）。この順でないと、途中で音が確認できない。

## 1. いま出来ていること

| | |
|---|---|
| ビルド | `dotnet build` が警告 0・エラー 0 で通る（`TreatWarningsAsErrors` 有効） |
| テスト | 42 件。`dotnet test` が全通過 |
| 起動 | `--host` / `--port` / `--voice` / `--concurrency` / `--list-voices` / `--help` |
| `GET /health` | [02](02-http-contract.md) §7 の形で返る |
| `GET /` | `--help` と同じテキスト |
| 未実装のパス | 404 + [02](02-http-contract.md) §6.1 の形の JSON |
| 検査 | `scripts\check.ps1`（BOM → build → format → test → check-contract） |
| スモーク | `node scripts/smoke.mjs`。合格 5 / 未実装 10 / 失敗 0 |
| フック | `.claude/hooks/cs-post-edit.ps1`。`*.cs` を編集したら差分ビルド、契約に触ったら照合 |
| サブエージェント | `contract-reviewer` / `winrt-interop-reviewer` |
| スキル | `smoke`（人も Claude も呼べる）/ `audio-contract`（Claude 専用の知識） |

## 2. 次にやること

### 2.1 `Speech/` — 合成のラッパ

`SynthesizeTextToStreamAsync` を呼んで **16 kHz の PCM を取り出す**ところまで。

決めておくこと:

- **`data` チャンクはチャンク走査で探す。** 44 バイト決め打ちは 2 バイトずれる（[03](03-windows-speech.md) §3）
- `IRandomAccessStream` を確実に閉じる。`SpeechSynthesisStream` は `IDisposable`
- `SpeechSynthesizer` をリクエストごとに作り直すか、使い回すか。**まず作り直しで測る。**
  使い回しは同時実行との兼ね合いが出るので、遅いと分かってからにする
- `CancellationToken` から WinRT の `IAsyncOperation.Cancel()` へ橋を架ける。
  接続が切れたら合成を止める（[02](02-http-contract.md) §11）ために要る

### 2.2 `Audio/` — 3 倍アップサンプラ

仕様は [02](02-http-contract.md) §5.2。**WinRT にも ASP.NET にも依存させない。**

検証は「実際に信号を通して測る」しかない:

- 1 kHz の正弦波を 16 kHz で作って 48 kHz へ上げ、**16 kHz と 32 kHz のイメージが十分落ちているか**
- 直流と無音が壊れないか
- 長さが正確に 3 倍になるか（端の処理で 1 サンプル増減しやすい）

### 2.3 `POST /v1/audio/speech`

[02](02-http-contract.md) §3〜§6 のとおり。実装順の勘所:

1. まず**全部合成してから 1 本のバイナリを返す**形で通す（`Content-Length` 付き）
2. 通ってから §4.3 のチャンク転送へ切り替える

いきなり 2 から入ると、「音がおかしい」のが合成なのかリサンプラなのかストリーミングなのか
切り分けられなくなる。

### 2.4 `/v1/models` と `/v1/audio/voices`

[02](02-http-contract.md) §8・§9。どちらも 10 行程度。§2.3 のついでに。

## 3. 決めたこと（蒸し返さないため）

### 3.1 `PublishTrimmed` は使わない

単一 exe は 60〜80 MB になる。`PublishTrimmed` で削れるが、**CsWinRT の projection は
リフレクションに依存する部分があり、トリミングで静かに壊れる**（起動はするのに
`AllVoices` が空になる、という類の壊れ方をする）。

代わりに `EnableCompressionInSingleFile` で圧縮する。展開のぶん初回起動が少し遅くなるが、
常駐サーバなので 1 回だけ。

**サイズが問題になったら、まず本当に問題なのかを測ること。**

### 3.2 SSE を実装しない

[02](02-http-contract.md) §4.2。合成が速すぎて刻む意味が無い。
**将来足すとしても、バイナリ経路は消さない。**

### 3.3 CORS ヘッダを付けない

[02](02-http-contract.md) §10。ループバックのローカルサーバに `Access-Control-Allow-Origin: *` を
付けると、任意の Web ページの JS から叩けるようになる。必要になったらオリジンを絞って足す。

### 3.4 既定の声に「日本語優先」を入れない

Windows の既定（`SpeechSynthesizer.DefaultVoice`）をそのまま使う。
日本語版 Windows なら結果的に日本語の声になる。英語版 Windows で英語の声が既定になるのは
**正しい挙動**で、起動ログに `voice=... (en-US)` と出るので気づける。

### 3.5 テストプロジェクトの TFM を本体と揃えた

`net9.0-windows10.0.19041.0`。素の `net9.0` だと `ProjectReference` が解決できない。
**`Audio/` は純粋なコードなので、将来テストだけ分離してマルチターゲットにする余地はある。**
いまは分けるほどの量が無い。

## 4. 引っかかったこと

### 4.1 `JsonSerializerOptions.MakeReadOnly()` は引数なしだと投げる

`TypeInfoResolver` を設定していない状態で `MakeReadOnly()` を呼ぶと
`InvalidOperationException`。`MakeReadOnly(populateMissingResolver: true)` を使う。
`Contract/ContractJson.cs` にコメントを残してある。

### 4.2 `.ps1` は UTF-8 の BOM 付きでないと日本語が壊れる

Windows PowerShell 5.1 は **BOM 無しの `.ps1` を CP932 として読む**。UTF-8 で保存すると、
日本語の文字列リテラルが**パース時点で**壊れる。実際にフックが返すメッセージが丸ごと化けた
（子プロセスから受け取ったテキストは無事だったので、原因の切り分けに手間取った）。

`.editorconfig` に `charset = utf-8-bom` を書き、`scripts\check.ps1` に BOM の検査を入れてある。
**`.ps1` を書き換えるツールの多くは BOM を落とす**ので、直したら検査を通すこと。

### 4.3 PowerShell から native コマンドの出力を `2>&1` で受けない

PowerShell は native コマンドの stderr を `ErrorRecord` に包む。
`$ErrorActionPreference = 'Stop'` だとそこで**終了エラー**になり、スクリプトが即死する。

フックが `node scripts/check-contract.mjs`（stderr に書く）を呼んだとき、
**exit 2 に到達せず exit 1 で終わっていた**。`dotnet build` は stdout に書くので、
こちらは正常に動いていた — 片方だけ壊れていたので気づきにくかった。

`Start-Process` で stdout / stderr をファイルへ分けて受けるように直した。
副産物として、`At line:… char:…` の飾りが本文に混ざらなくなった。

### 4.4 `dotnet format` は編集のたびに走らせるには遅い

1 ファイル指定でも **3.7 秒**（実測。`dotnet build` の差分は 1.3 秒）。
フックから外して `scripts\check.ps1` に移した。`-Fix` を付けるとその場で直る。

### 4.5 PowerShell 5.1 でログを読むと文字化けする

サーバの stdout は **UTF-8**。`Get-Content` は既定で ANSI（この PC では CP932）として読むので、
日本語が化ける。**`Get-Content -Encoding UTF8` を付ける。**
サーバ側の出力は壊れていない。

### 4.6 stdout の 1 行目を機械可読にするため、ログは全部 stderr へ出している

`Now listening on: ...` のような行が先に出ると `OPENAI_WINDOWS_TTS_PORT=` が 1 行目でなくなる。
`ConsoleLoggerOptions.LogToStandardErrorThreshold = Trace` で全レベルを stderr へ回している。
**ログを stdout に戻すと `scripts/smoke.mjs` が壊れる。**

## 5. 保留にしていること

| | いつ考えるか |
|---|---|
| 単語・文の境界メタデータ（字幕の時刻） | `IncludeSentenceBoundaryMetadata` で取れる。字幕が欲しくなったら |
| 声ごとの発音辞書 | Windows 側にユーザ辞書の口はあるが、サーバから触ると PC 全体に影響する |
| Windows サービスとして登録 | 「置いて叩くだけ」を壊すので、要望が出るまでやらない |
| 複数プロセスでの負荷分散 | `--concurrency` で足りなくなってから |
