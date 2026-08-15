# voxbridge

**Windows に最初から入っている音声を、OpenAI 互換の HTTP API で喋らせる常駐サーバ。**

GPU も、モデルのダウンロードも要りません。exe を 1 つ置いて叩くだけです。

> **OpenAI とも Microsoft とも無関係の、独立した実装です。**
> `/v1/audio/speech` という API の**形だけ**を合わせ、Windows の音声合成を呼び出しています。
> どちらの企業からも承認・後援を受けていません。

> **位置づけ**: 高品質な TTS エンジンの代わりではありません。声の質と機能の落差は大きく、
> 参照音声も、話し方の文章指定も、seed による作り分けもありません。
> **VRAM に余裕が無い環境や、とにかく喋らせたい場面のための軽い選択肢**です。

## 使う

```
voxbridge.exe
```

既定で `http://127.0.0.1:8288` を待ち受けます。

```
$ curl http://127.0.0.1:8288/health
{"status":"ok","model":"Windows 内蔵音声 / Microsoft Ayumi（参照音声・caption・seed は非対応）", ...}

$ curl -X POST http://127.0.0.1:8288/v1/audio/speech \
    -H "Content-Type: application/json" \
    -d '{"input":"こんにちは","voice":"ayumi"}' \
    -o hello.wav
```

OpenAI 互換のクライアントからは、接続先の URL を `http://127.0.0.1:8288` に向けるだけで繋がります。

### 起動オプション

```
--host <addr>          既定 127.0.0.1
--port <n>             既定 8288。0 を渡すと OS が空きポートを割り当てる
--voice <id|name>      既定の声。省略すると Windows の既定の声
--concurrency <n>      同時に受け付けるリクエスト数（既定 1）
--queue-timeout <sec>  実行枠が空くのを待つ上限（既定 30）。超えたら 503
--verbose              合成 1 件につき 1 行ログを出す（stderr）
--list-voices          この PC の声を並べて終了する
-h, --help             ヘルプ
```

既定ポートが 8288 なのは、8088 / 8188 を使う TTS サーバとぶつからないようにするためです。
同じ PC で並べて起動し、聞き比べられます。

### 使える声

```
$ voxbridge.exe --list-voices
ayumi             Microsoft Ayumi  (ja-JP, Female)
haruka            Microsoft Haruka  (ja-JP, Female)
ichiro            Microsoft Ichiro  (ja-JP, Male)
```

声は**この PC に入っているものだけ**です。追加するには
`設定 > 時刻と言語 > 言語と地域` から音声パッケージを入れてください。

リクエストごとに `voice`（または `model`）で切り替えられます。

## できないこと

Windows の音声合成に対応する機能が無いものは、**受け取っても黙って捨てます**（エラーにはしません）。
そのことは `/health` の `model` 名で名乗ります。

| | |
|---|---|
| 参照音声（`ref_wav`） | 声を真似る機能がありません |
| 話し方の文章指定（`caption`） | ありません |
| `seed` / ステップ数 / CFG | 生成モデルではないので概念がありません |
| mp3 / flac での出力 | wav のみ返します |
| SSE での進捗通知 | 合成が速い（**1.1 ms/文字**、3,000 文字で 12 分ぶんの音声を 3.3 秒）ので刻んでいません |
| 認証・TLS | ありません。ループバック前提です |

長い文章を渡すと、合成が終わる前にヘッダを返して流し始めます（チャンク転送）。
3,000 文字でもヘッダは **0.5 秒**以内に返るので、接続タイムアウトを踏みません。

## 動作環境

- Windows 10 1809 以降 / Windows 11
- 日本語の音声パッケージ（多くの日本語版 Windows には最初から入っています）

配布用の単一 exe には .NET ランタイムを同梱しているので、**.NET のインストールは不要**です。

## 作る

```
dotnet build
dotnet test
dotnet run --project src/VoxBridge -- --port 8288
```

配布用の単一 exe（**52 MB**・.NET ランタイム同梱）:

```
dotnet publish src/VoxBridge -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish
```

初回起動だけ圧縮の展開に 2.4 秒ほどかかります（2 回目以降は 0.4 秒）。

設計と契約は [docs/](docs/) にあります。開発の作法は [CLAUDE.md](CLAUDE.md)。

## ライセンス

**このプロジェクトのコードは [MIT ライセンス](LICENSE)** です。

**配布用の単一 exe には Microsoft のコンポーネントが同梱されます。**
.NET ランタイムは MIT、Windows SDK の .NET projection
（`Microsoft.Windows.SDK.NET.dll` / `WinRT.Runtime.dll`）は
[Windows SDK のライセンス条項](https://aka.ms/WinSDKLicenseURL)が適用されます。
[再頒布可能ファイルの一覧](https://learn.microsoft.com/en-us/legal/windows-sdk/redist)に
名指しで挙げられているので**同梱と再頒布は認められています**が、条件が付きます。

詳細は **[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)** を読んでください。

### 生成した音声の扱い

**Microsoft は、Windows 内蔵の音声合成が出力した音声データの利用範囲について
明示していません。** Windows の使用許諾条件にも SDK のライセンス条項にも、
音声合成の出力に関する条項がありませんでした（2026-08-15 時点で確認）。

**MIT ライセンスはこのプロジェクトのコードにのみ適用され、生成された音声には及びません。**
商用利用など、用途によってはご自身で Microsoft に確認してください。
