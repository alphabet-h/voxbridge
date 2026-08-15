# 08 申し送り

> **最終更新: 2026-08-15**
>
> **いまどこにいるか**: **MVP は動いています。** `POST /v1/audio/speech` が
> 48 kHz / 1ch / 16bit の WAV を返し、`/health` `/v1/models` `/v1/audio/voices` も揃いました。
> テスト 160 件・契約適合スモーク 21 項目がすべて通ります。
> レビューで見つかった**プロセス即死バグ（§4.0）を含む 5 件は修正済み**です。
>
> **次にやること**: §2。急ぐものはありません。**配布（§2.1）を試すのが自然な次の一歩**です。

## 1. いま出来ていること

| | |
|---|---|
| `POST /v1/audio/speech` | 48 kHz / 1ch / 16bit。1,000 文字以下は `Content-Length` 付き、超えたらチャンク転送 |
| 声の切り替え | `voice` / `model` / 起動時の `--voice`。`none` は「指定なし」、未知の声は 400 |
| `speed` | 0.25〜4.0 を受け、Windows の有効域 0.5〜6.0 へクランプ |
| 非対応のパラメータ | `caption` / `seed` / `ref_wav` などは**読まない**。`/health` の `model` で名乗る |
| `/health` | `model` / `gpu` / `max_concurrent_synthesis` / `running` / `queued` |
| 同時実行 | **合成は常に 1 本ずつ**（変更不可・§4.0）。`--concurrency` は受け付ける本数、`--queue-timeout` 超過で 503 + `Retry-After` |
| キャンセル | 接続断で合成を止めて実行枠を返す |
| エラー | 400 / 413 / 404 / 500 / 503 すべて契約の形の JSON |
| 起動オプション | `--host` `--port` `--voice` `--concurrency` `--queue-timeout` `--verbose` `--list-voices` `--help` |
| 検査 | `scripts\check.ps1`（BOM → build → format → test → 契約照合）/ テスト 160 件 |
| スモーク | `node scripts/smoke.mjs` — 21 項目すべて OK |
| 配布 | **単一 exe を publish して確認済み**（下記 §1.1） |
| **実際の音** | **耳で確認済み**（2026-08-15）。3 声とも本文が聞き取れ、ayumi / haruka は女性、ichiro は男性 |

**音は聞かないと分からない。** RMS もイメージ抑圧も継ぎ目の長さも数値で押さえてあるが、
「合成 → 3 倍アップサンプル → WAV → HTTP」を通した先で**言葉として聞こえるか**は
どの検査にも入っていない。音に関わる変更をしたら、毎回聞くこと。

### 1.1 配布（2026-08-15 実施）

```
dotnet publish src/VoxBridge -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish
```

| | |
|---|---|
| サイズ | **52.4 MB**（`EnableCompressionInSingleFile` あり） |
| publish にかかる時間 | 66 秒 |
| **WinRT の projection** | **動く。** 単一 exe から 3 声とも引ける |
| 初回起動（展開あり） | **2,436 ms** |
| 2 回目以降（展開キャッシュ後） | **400 ms** |
| 契約適合スモーク | `node scripts/smoke.mjs --exe publish\voxbridge.exe` で **21 項目すべて OK** |

初回だけ 2.4 秒かかるのは圧縮の展開ぶん。常駐サーバなので 1 回だけ。
`publish/` は `.gitignore` 済み。

## 2. 次にやること

### 2.1 `SpeechSynthesizer` の使い回し

いまはリクエストごとに作って捨てています（[03](03-windows-speech.md)）。
使い回すと速くなるかもしれませんが、同時実行との兼ね合いが出ます。
**遅いと分かってから**にします。

### 2.2 区切りの無い長文でのトリム

文の区切りが 1,000 文字の中に 1 つも無いテキストは、読点や文字数で強制的に切るので、
**文の途中に 1,080 ms の間が入ります**（[03](03-windows-speech.md) §8）。
実害が出たら、内部境界に限って無音をトリムします。

### 2.3 字幕の時刻

`SpeechSynthesizerOptions.IncludeSentenceBoundaryMetadata` で文の境界時刻が取れます。
字幕を出したくなったときの手がかり。**契約に載せる口がまだ無い**ので、その設計から。

## 3. 決めたこと（蒸し返さないため）

### 3.1 `PublishTrimmed` は使わない

単一 exe は 60〜80 MB になります。`PublishTrimmed` で削れますが、**CsWinRT の projection は
リフレクションに依存する部分があり、トリミングで静かに壊れる**（起動はするのに
`AllVoices` が空になる、という類の壊れ方をする）。

代わりに `EnableCompressionInSingleFile` で圧縮します。
**サイズが問題になったら、まず本当に問題なのかを測ること。**

### 3.2 SSE を実装しない

[02](02-http-contract.md) §4.2。合成が速すぎて刻む意味が無い。
**将来足すとしても、バイナリ経路は消さない。**

### 3.3 CORS ヘッダを付けない

[02](02-http-contract.md) §10。ループバックのローカルサーバに `Access-Control-Allow-Origin: *` を
付けると、任意の Web ページの JS から叩けるようになる。必要になったらオリジンを絞って足す。

### 3.4 既定の声に「日本語優先」を入れない

Windows の既定（`SpeechSynthesizer.DefaultVoice`）をそのまま使う。
英語版 Windows で英語の声が既定になるのは**正しい挙動**で、
起動ログに `voice=... (en-US)` と出るので気づけます。

### 3.5 テストプロジェクトの TFM を本体と揃えた

`net9.0-windows10.0.19041.0`。素の `net9.0` だと `ProjectReference` が解決できません。
**`Audio/` は純粋なコードなので、将来テストだけ分離してマルチターゲットにする余地はある。**

### 3.6 継ぎ目の無音はトリムしない（実測して決めた）

**継ぎ目 1,080 ms = 文と文の自然な間 1,080 ms** で完全に一致しました（[03](03-windows-speech.md) §8）。
文の区切りでしか分割しないので、継ぎ目は聞き分けられません。**トリムは足さない。**

### 3.7 1,000 文字以下は分割しない

分割は継ぎ目を作ります。必要のない場面で背負う理由がありません。
1,000 文字ならヘッダまで 1.3 秒で、接続タイムアウトには遠い（[02](02-http-contract.md) §4.3）。

### 3.8 リクエスト本文は自分で読む

Minimal API の自動バインドを使わず、`JsonSerializer.DeserializeAsync` で読んでいます。
理由は §4.1。

### 3.9 名前を `voxbridge` にした（2026-08-15）

以前の名前には `Windows` と `OpenAI` が入っていました。**どちらも他社の商標**です。

[Microsoft の商標ガイドライン](https://www.microsoft.com/en-us/legal/intellectualproperty/trademarks/usage/general)は
「Microsoft のブランド資産を製品名・アプリ名・ドメイン名に使わない」と明記する一方、
「〜と互換性がある」という**参照的な記述は明示的に許可**しています。

さらに、このプロジェクトは**単一 exe に Microsoft の再頒布可能コードを同梱する**ので、
Windows SDK ライセンス条項 §2.a.iii の「製品名に Microsoft の商標を使わない」が
**契約上の義務として効きます**（`THIRD-PARTY-NOTICES.md`）。

名前に `Windows` を含むサードパーティ製ツールは実在しますが、
それらは Microsoft の DLL を配っていないので、この契約の当事者ではありません。
**同梱するかどうかが分かれ目です。**

README では「Windows の音声合成を使う」という参照的な書き方にとどめてあります。

### 3.10 ライセンスは MIT。ただし配布物には条件が付く（2026-08-15）

- **コードは MIT**（`LICENSE`）
- **単一 exe には Microsoft のコンポーネントが入る。** 再頒布は明示的に認められているが、
  著作権表示・未改変・商標の 3 点が条件（`THIRD-PARTY-NOTICES.md`）
- `Directory.Build.props` の `Copyright` は**その条件を満たすためのもの。消さないこと**
- **生成した音声の利用範囲は Microsoft が明示していない。** 一次資料に許可も禁止も無かった。
  README に「各自で確認を」と書いてある。**「制限なし」と書かないこと** — 根拠が無い

### 3.11 リサンプラはここで打ち止め（2026-08-15 に測って決めた）

**先に判断の基準を決めてから測りました**（合成に対して 2 割未満なら直さない / 2〜5 割なら
ビット単位で同じ結果を保つ範囲だけ / 5 割超なら確保の削減まで）。

測った結果は **Release で合成の 35%**。基準どおり、ビット単位で同じ結果を保つ 2 つだけやりました:

- **入力サンプルで回して 3 出力をまとめて出す。** 出力ごとに回すと同じ窓 17 個を 3 回読み直す
- 係数のジャグ配列を 1 本に平坦化し、端の境界判定を内側のループから追い出す

結果 **0.77 ms/音声1秒（38% 短縮）**、合成に対して **約 20%**。基準の「2 割未満」に乗ったので止めました。

**やらなかったこと**:

- `double` → `float`: 結果がビット単位で変わる。実測済みの -103 dB を測り直すことになる
- バイト列への直接書き出しと `ArrayPool`: 確保は 1,000 文字のチャンクで 38.5 MB あるが、
  時間としては 0.08 ms/音声1秒（全体の 1 割弱）しかない。**時間で問題が出ていないものを先回りして直さない**

再開するときは `Saturate` の `Math.Round(..., AwayFromZero)` から。ただし**まず測ること**。

## 4. 引っかかったこと

### 4.0 合成を同時に呼ぶとプロセスが落ちる（最重要）

`--concurrency 2` 以上で、**例外ではなく `__fastfail` でプロセスごと落ちていました**。
詳細と実測は [03](03-windows-speech.md) §5。`Speech/SpeechEngine` の `SemaphoreSlim(1,1)` で直列化して解決。

**`--help` に載っている公開オプションを 2 にするだけで利用者がサーバを落とせる状態**でした。
自動テストでは長さが同じなので捕まらず、レビューで並列に叩いて初めて出ました。
回帰テストは `SpeechEngineTests.並列に呼んでもプロセスが落ちない`。

### 4.05 話速のクランプが、防ごうとした不具合を自分で作っていた

`MinSpeakingRate = 0.5` にしていたせいで、契約上の有効値 `speed: 0.25` が
`0.5` とバイト単位で同一の WAV を返していました。**実測では 0.2 まで効きます**
（[03](03-windows-speech.md) §4）。ドキュメントの数値を信じて実測しなかったのが原因。

### 4.06 プロセス最初の 1 回だけ音が違う

[03](03-windows-speech.md) §6。起動時に捨て合成（`SpeechEngine.WarmUpAsync`）を入れて解決。

### 4.07 `MapFallback` はドットを含むパスに一致しない

引数なしの `MapFallback` は `{*path:nonfile}` を使うため、
`/openapi.json` や `/v1/audio/speech.wav` が**本文 0 バイト・Content-Type なしの 404** に
なっていました。契約が防ごうとしていた「404 の本文をパースできない」がそのまま起きます。

`app.MapFallback("{*path}", ...)` とパターンを明示して解決。
スモークにドット入りパスの検査を追加してあります。

### 4.08 キャンセル集中でハンドルとメモリが一時的に太る（実害なし）

150 件を 60 ms で abort すると、ハンドル 668 → 2340、プライベートバイト 79 → 180 MB。
`AsTask(cancellationToken)` でキャンセルすると `SpeechSynthesisStream` が誰にも閉じられずに残るため。
**GC 後は 649 / 81 MB に戻るので恒久的なリークではありません。** 対処なし。

### 4.09 速さを 2 回とも間違った測り方で測っていた

「リサンプラが合成時間の 3〜4 割」という §2 の記述は、**Debug ビルド**で、しかも
**ウォームアップせずに 1 回だけ**測った数字でした。Release で温めて測ると別の結論になります
（[03](03-windows-speech.md) §7.1 に詳細）。

- Debug は**リサンプラが 4.7 倍遅く出る**。「変換が合成の 160%」→ Release では 35% と結論が反転する
- 合成の初回は WinRT の初期化を含む。1 文で 198 ms 出たが、温まると 0.90 ms/文字

**プローブで測るときは、Release で、3 回捨ててから中央値を採ること。**
`docs/08` に「まず本当に問題なのかを測ること」と自分で書いておいて、
その測り方を間違えていました。

### 4.1 Minimal API の自動バインドは、壊れた JSON で**例外を投げない**

`SpeechRequest?` をハンドラの引数にすると、JSON が壊れているときに
**例外を投げずに、その場で本文の無い 400 を書いて打ち切ります**。
ミドルウェアまで届かないので、契約どおりのエラー body を返せません。

実測: `Request finished ... 400 0 -` — Content-Type すら付かない空の応答。
413（本文超過）も同じ。

**本文は `JsonSerializer.DeserializeAsync(http.Request.Body, ...)` で自分で読む。**
そうすれば `JsonException` と `BadHttpRequestException` がミドルウェアまで上がってきます。
Content-Type を見ないので、付けてこないクライアントも通ります。

### 4.2 `--verbose` は自分のカテゴリだけ上げる

`SetMinimumLevel(Information)` にすると `Microsoft.AspNetCore.*` のリクエストログで溢れ、
**肝心の 1 行が埋もれます**。`AddFilter("synthesis", Information)` でカテゴリを絞ること。

### 4.3 `ContractException` をエラーとしてログしない

503 や 400 は**契約で決めた応答**であって不具合ではありません。
スタックトレース付きで `LogError` すると、正常系でログが埋まります。`LogDebug` へ。

### 4.4 本文を読まないクライアントは実行枠を握り続ける

チャンク転送はクライアントが読んだぶんだけ進みます。ヘッダだけ受けて本文を放置されると
TCP のバックプレッシャでサーバの書き込みが止まり、**実行枠を握ったまま**になります。

検証スクリプトを書いていて自分で踏みました（2 本目が 30 秒待って 503 になった）。
上限は `--queue-timeout` が担保します。**仕様として受け入れる。**

### 4.5 `.ps1` は UTF-8 の BOM 付きでないと日本語が壊れる

Windows PowerShell 5.1 は **BOM 無しの `.ps1` を CP932 として読む**。UTF-8 で保存すると、
日本語の文字列リテラルが**パース時点で**壊れます。

`.editorconfig` に `charset = utf-8-bom` を書き、`scripts\check.ps1` に BOM の検査を入れてあります。
**`.ps1` を書き換えるツールの多くは BOM を落とす**ので、直したら検査を通すこと。

### 4.6 PowerShell から native コマンドの出力を `2>&1` で受けない

PowerShell は native コマンドの stderr を `ErrorRecord` に包みます。
`$ErrorActionPreference = 'Stop'` だとそこで**終了エラー**になり、スクリプトが即死します。

フックが `node scripts/check-contract.mjs`（stderr に書く）を呼んだとき、
**exit 2 に到達せず exit 1 で終わっていました**。`dotnet build` は stdout に書くので
そちらは正常に動いており、片方だけ壊れていたので気づきにくい状態でした。

### 4.7 `JsonSerializerOptions.MakeReadOnly()` は引数なしだと投げる

`TypeInfoResolver` を設定していない状態で呼ぶと `InvalidOperationException`。
`MakeReadOnly(populateMissingResolver: true)` を使います。

### 4.8 `dotnet format` は編集のたびに走らせるには遅い

1 ファイル指定でも **3.7 秒**（`dotnet build` の差分は 1.3 秒）。
フックから外して `scripts\check.ps1` に移しました。`-Fix` を付けるとその場で直ります。

### 4.9 PowerShell 5.1 でログを読むと文字化けする

サーバの stdout は **UTF-8**。`Get-Content` は既定で CP932 として読むので日本語が化けます。
**`Get-Content -Encoding UTF8` を付ける。** サーバ側の出力は壊れていません。

### 4.10 stdout の 1 行目を機械可読にするため、ログは全部 stderr へ出している

`Now listening on: ...` のような行が先に出ると `VOXBRIDGE_PORT=` が 1 行目でなくなります。
`ConsoleLoggerOptions.LogToStandardErrorThreshold = Trace` で全レベルを stderr へ回しています。
**ログを stdout に戻すと `scripts/smoke.mjs` が壊れます。**

## 5. 保留にしていること

| | いつ考えるか |
|---|---|
| 声ごとの発音辞書 | Windows 側にユーザ辞書の口はあるが、サーバから触ると PC 全体に影響する |
| Windows サービスとして登録 | 「置いて叩くだけ」を壊すので、要望が出るまでやらない |
| 複数プロセスでの負荷分散 | `--concurrency` で足りなくなってから |
| リポジトリのフォルダ名 | 中身は `voxbridge` へ改名済みだが、**フォルダ名だけ旧名のまま**。変えるときは Claude 側のプロジェクト記憶（`~/.claude/projects/`）も移す |
