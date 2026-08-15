# 03 Windows の音声合成

> この文書の数値は **2026-08-15 に Windows 11 Pro build 26200 の 1 台**で実測したもの。
> 台数 1 台の観測なので、別の PC で違ったら**この表を疑うのではなく、両方を書く**こと。

## 1. どの API を使うか

Windows から音声合成を呼ぶ道は 2 本ある。**WinRT を使う。**

| | 使える日本語の声 | 出力フォーマット |
|---|---|---|
| **WinRT** `Windows.Media.SpeechSynthesis` | **3 つ** — Ayumi / Haruka / Ichiro | 16 kHz 固定・変更不可 |
| `System.Speech`（SAPI5） | **Haruka Desktop だけ** | `SpeechAudioFormatInfo` で指定できる（48 kHz も直接出る） |

`System.Speech` は出力フォーマットを選べるという利点があるが、**OneCore の声が見えない**。
実測では `System.Speech` から見えたのは `Microsoft Haruka Desktop` と `Microsoft Zira Desktop`（en-US）の 2 つだけだった。

OneCore の声を SAPI5 側へ見せる道具は存在するが、**利用者に別途インストールを強いる**。
「置いて叩くだけ」を壊すので採らない。

**3 声を取り、リサンプルは自分でやる。**

## 2. この PC の声

```
ayumi   Microsoft Ayumi    ja-JP  Female   MSTTS_V110_jaJP_AyumiM
haruka  Microsoft Haruka   ja-JP  Female   MSTTS_V110_jaJP_HarukaM
ichiro  Microsoft Ichiro   ja-JP  Male     MSTTS_V110_jaJP_IchiroM
```

`VoiceInformation.Id` の実体は `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Speech_OneCore\Voices\Tokens\...` という
レジストリのフルパス。**API に載せるには長すぎる**ので、表示名から短い id を作る
（`Speech/VoiceCatalog.MakeId`）。

`--list-voices` で同じ一覧が出る。

## 3. 出力フォーマット — ここが設計を決めた

`SynthesizeTextToStreamAsync` が返すストリームの中身:

| | |
|---|---|
| `ContentType` | `audio/wav` |
| サンプルレート | **16,000 Hz** |
| チャンネル | 1 |
| ビット深度 | 16 |
| `fmt` チャンク長 | **18**（`cbSize = 0` が付く） |
| `data` チャンクの開始 | offset **38** |
| PCM 本体の開始 | offset **46** |

**フォーマットを指定する API は存在しない。** `SpeechSynthesizerOptions` に該当する項目が無い（§4）。

ここから 2 つ決まる:

1. **48 kHz へ 3 倍にアップサンプルする必要がある**（[02](02-http-contract.md) §5.2）
2. **ヘッダは 44 バイトではなく 46 バイト。** 44 バイト決め打ちで PCM を切り出すと 2 バイトずれて、
   全体が半サンプルずれた雑音混じりの音になる。**必ずチャンクを走査して `data` を探すこと**

実際のヘッダ（先頭 46 バイト）:

```
52 49 46 46 a6 19 02 00  RIFF <size>
57 41 56 45              WAVE
66 6d 74 20 12 00 00 00  "fmt " 18
01 00                    PCM
01 00                    1ch
80 3e 00 00              16000 Hz
00 7d 00 00              32000 byte/s
02 00                    blockAlign 2
10 00                    16bit
00 00                    cbSize 0     ← 44 バイト固定のヘッダには無い 2 バイト
64 61 74 61 80 19 02 00  "data" <size>
```

## 4. `SpeechSynthesizerOptions`

| プロパティ | 型 | 既定 | 使うか |
|---|---|---|---|
| `SpeakingRate` | double | 1.0 | **使う。** `speed` を写す |
| `AudioVolume` | double | 1.0 | 使わない（音量は呼ぶ側で） |
| `AudioPitch` | double | 1.0 | 使わない |
| `AppendedSilence` | enum | `Default` | 使わない。`Default` / `Min` の 2 値しかなく、数値では指定できない |
| `PunctuationSilence` | enum | `Default` | 同上 |
| `IncludeWordBoundaryMetadata` | bool | false | いまは使わない。字幕の時刻を出す口としては見込みがある |
| `IncludeSentenceBoundaryMetadata` | bool | false | 同上 |

### `SpeakingRate` の落とし穴

**setter は値を検証しない。** 0.25 も 6.5 も例外を投げずに通り、読み返すとその値が返ってくる。
ドキュメント上の有効域は **0.5〜6.0** なので、範囲外は合成時に黙って丸められる。

**クランプはこちら側でやる**（[02](02-http-contract.md) §3.4）。やらないと
「0.3 を設定できたのに速度が変わらない」という、API からは正常に見える不具合になる。

## 5. 速さ

| | |
|---|---|
| 合成 | 19 文字の文（4.3 秒の音声）で **73 ms**。RTF ≒ **0.017** |
| GPU | 不要 |
| `powershell.exe` の起動 | **1,834 / 2,180 / 2,606 ms**（3 回計測） |

**プロセス起動が合成の 15〜22 倍かかる**ので、「都度コマンドを叩く」方式は成立しない。
常駐サーバにする根拠はここ（[01](01-overview.md) §2）。

合成がこれだけ速いので、進捗を SSE で刻む意味はほぼ無い（[02](02-http-contract.md) §4.2）。

## 6. API の面

`SpeechSynthesizer` が持つメソッドは 2 つだけ。

- `SynthesizeTextToStreamAsync(string)`
- `SynthesizeSsmlToStreamAsync(string)`

SSML は受け付けない方針（[01](01-overview.md) §5）。利用者の本文がそのまま
マークアップとして解釈される口を開けると、`<` を含むテキストで壊れる。

静的プロパティ:

- `SpeechSynthesizer.AllVoices` — 全部の声
- `SpeechSynthesizer.DefaultVoice` — Windows の既定（表示言語に追随する）

**既定の声はここから取る。** 「日本語の声を優先する」のような細工をこちらで入れると、
起動ログに出ている声と実際に喋る声が食い違ったときに理由が追えなくなる。
選びたければ `--voice` で明示する。

## 7. 確認のしかた

PowerShell から声の一覧だけなら:

```powershell
$null = [Windows.Media.SpeechSynthesis.SpeechSynthesizer, Windows.Media, ContentType=WindowsRuntime]
[Windows.Media.SpeechSynthesis.SpeechSynthesizer]::AllVoices |
  ForEach-Object { "{0} | {1} | {2}" -f $_.DisplayName, $_.Language, $_.Gender }
```

合成まで確かめるなら、`WindowsRuntimeSystemExtensions.AsTask` を反射で引いて
`IAsyncOperation` を待つ必要があり、PowerShell 5.1 では手数が多い。
**素直に `dotnet run -- --list-voices` を使うほうが早い。**
