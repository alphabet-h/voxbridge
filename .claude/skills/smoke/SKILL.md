---
name: smoke
description: サーバを実際に起動して HTTP 契約の適合を叩く。/v1/audio/speech・/health・エラー応答・WAV のフォーマットに触ったあと、コミット前、「動くか確かめて」「スモーク」「smoke」「契約が守れているか見て」と言われたときに使う。
---

# 契約適合スモーク

サーバを **`--port 0`** で起動し、`docs/02-http-contract.md` のとおりに応答するかを一通り叩く。

## 走らせる

```
dotnet build
node scripts/smoke.mjs
```

ビルドを先に通すこと。`smoke.mjs` は `src/OpenAiWindowsTts/bin/{Debug,Release}/*/openai-windows-tts.exe`
を探す。見つからなければ終了コード 2 で「先に dotnet build を通してください」と言う。

| オプション | |
|---|---|
| `--exe <path>` | exe を明示する（publish した単一 exe を確かめるとき） |
| `--keep` | 失敗したときサーバを落とさない。`curl` で手で叩いて調べられる |

## 出力の読み方

```
OK   GET /health が契約どおりの形を返す
SKIP 合成した WAV が 48000Hz / 1ch / 16bit  — 未実装
NG   知らない voice は 400 / VOICE_NOT_FOUND  — status=200
```

- **`SKIP` は失敗ではない。** そのエンドポイントがまだ 404 / `NOT_FOUND` を返している、
  という意味。実装すると自動で `OK` か `NG` に変わる
- **`SKIP` が減らない実装をしたら疑う。** 「実装したのに SKIP のまま」は、
  404 を返し続けている（ルーティングを繋ぎ忘れた）ということ
- 終了コードは `NG` が 1 件でもあれば 1、それ以外は 0

## 何を確かめているか

`scripts/smoke.mjs` に全部書いてあるが、外せない項目は以下。**減らさないこと。**

| | なぜ外せないか |
|---|---|
| 48000Hz / 1ch / 16bit | 割ると、サンプルレートを変換しないクライアントで生成が全件失敗する |
| `voice: "none"` が通る | 「指定なし」の意味で送る実装がある。弾くと全リクエストが落ちる |
| `caption` / `ref_wav` を送っても 200 | 400 にすると、参照音声を 1 つ設定しただけで全件失敗する |
| エラー本文に参照音声のパスが無い | パスが出たことを根拠に「参照音声の失敗」と判定するクライアントがある |
| `stream_format: "sse"` が 400 にならない | 常に付けてくるクライアントがある |
| `/health` の `gpu` が空でない | 省くと `nvidia-smi` を叩いて PC の GPU を表示するクライアントがある |
| `/health` の `model` が非対応機能を名乗る | 落差を利用者に伝えられる唯一の口 |

## スモークが通らないとき

1. まず `powershell -NoProfile -File scripts\check.ps1` を通す。ビルドと契約の照合が先
2. `node scripts/smoke.mjs --keep` で起動したまま止め、`curl` で該当のリクエストを手で叩く
3. サーバのログは **stderr** に出る。stdout は機械可読の 1 行目のために空けてある
4. PowerShell でログを読むときは **`Get-Content -Encoding UTF8`**。付けないと日本語が化ける

## 注意

- `dotnet run` ではなく exe を直に叩いている。`dotnet run` はトランポリンなので、
  親を殺しても孫がポートを掴んだまま残る
- 固定ポートは使わない。開発中のサーバが 8288 で動いていてもスモークは通る
