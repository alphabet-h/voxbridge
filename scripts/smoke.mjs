#!/usr/bin/env node
/**
 * サーバを起動して、docs/02-http-contract.md のとおりに応答するかを叩く。
 *
 * 使い方:
 *   node scripts/smoke.mjs                 … Debug ビルドの exe を探して使う
 *   node scripts/smoke.mjs --exe <path>    … exe を明示する
 *   node scripts/smoke.mjs --keep          … 失敗したときサーバを落とさない（手で叩いて調べる）
 *
 * 決めごと:
 *
 *   1. **--port 0 で起動し、stdout の 1 行目から実ポートを読む。**
 *      固定ポートにすると、開発中のサーバが動いているだけでスモークが落ちる。
 *
 *   2. **dotnet run ではなく exe を直に叩く。**
 *      dotnet run はトランポリンなので、親を殺しても孫がポートを掴んだまま残る。
 *
 *   3. **未実装のエンドポイントは SKIP にして落とさない。**
 *      「まだ書いていない」と「壊れた」を混ぜると、実装中ずっと赤いままになって
 *      スモークを見なくなる。実装したら自動で緑になる。
 *
 * 依存ゼロ。node:child_process / node:fs / node:path だけを使う。
 */

import { spawn } from 'node:child_process'
import { existsSync, readdirSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..')
const EXE_NAME = 'voxbridge.exe'
const PORT_PREFIX = 'VOXBRIDGE_PORT='
const STARTUP_TIMEOUT_MS = 30_000

/* ============================================================
 * 1. 起動オプション
 * ============================================================ */

const argv = process.argv.slice(2)
let exePath = null
let keepAlive = false

for (let i = 0; i < argv.length; i++) {
  if (argv[i] === '--exe') exePath = argv[++i]
  else if (argv[i] === '--keep') keepAlive = true
  else if (argv[i] === '--help' || argv[i] === '-h') {
    process.stdout.write('使い方: node scripts/smoke.mjs [--exe <path>] [--keep]\n')
    process.exit(0)
  } else {
    process.stderr.write(`不明なオプションです: ${argv[i]}\n`)
    process.exit(2)
  }
}

function findExe() {
  const binRoot = join(ROOT, 'src', 'VoxBridge', 'bin')
  if (!existsSync(binRoot)) return null

  for (const configuration of ['Debug', 'Release']) {
    const configurationDir = join(binRoot, configuration)
    if (!existsSync(configurationDir)) continue

    for (const framework of readdirSync(configurationDir)) {
      const candidate = join(configurationDir, framework, EXE_NAME)
      if (existsSync(candidate)) return candidate
    }
  }
  return null
}

exePath ??= findExe()
if (exePath === null || !existsSync(exePath)) {
  process.stderr.write(`${EXE_NAME} が見つかりません。先に dotnet build を通してください。\n`)
  process.exit(2)
}

/* ============================================================
 * 2. 検査の枠組み
 * ============================================================ */

const results = []

async function check(name, body) {
  try {
    const skipReason = await body()
    results.push({ name, state: skipReason ? 'skip' : 'ok', note: skipReason ?? '' })
  } catch (error) {
    results.push({ name, state: 'ng', note: error.message })
  }
}

function expect(condition, message) {
  if (!condition) throw new Error(message)
}

class NotImplemented extends Error {}

/** 未実装（404 / NOT_FOUND）なら SKIP に落とす。他の失敗はそのまま投げる */
async function skipIfNotImplemented(body) {
  try {
    return await body()
  } catch (error) {
    if (error instanceof NotImplemented) return '未実装'
    throw error
  }
}

/* ============================================================
 * 3. WAV の検査（チャンクを走査する。44 バイト決め打ちにしない）
 * ============================================================ */

function parseWav(buffer) {
  expect(buffer.length >= 12, `WAV が短すぎます（${buffer.length} バイト）`)
  expect(buffer.toString('ascii', 0, 4) === 'RIFF', 'RIFF で始まっていません')
  expect(buffer.toString('ascii', 8, 12) === 'WAVE', 'WAVE ではありません')

  let format = null
  let dataLength = null

  let offset = 12
  while (offset + 8 <= buffer.length) {
    const id = buffer.toString('ascii', offset, offset + 4)
    const declared = buffer.readUInt32LE(offset + 4)
    const body = offset + 8
    const available = buffer.length - body
    // ストリーミング書き出しの WAV は size が 0 のことがある。残り全部として読む
    const size = declared === 0 || declared > available ? available : declared

    if (id === 'fmt ' && size >= 16) {
      format = {
        audioFormat: buffer.readUInt16LE(body),
        channels: buffer.readUInt16LE(body + 2),
        sampleRate: buffer.readUInt32LE(body + 4),
        bitDepth: buffer.readUInt16LE(body + 14),
      }
    } else if (id === 'data') {
      dataLength = size
    }

    offset = body + size + (size % 2)
  }

  expect(format !== null, 'fmt チャンクがありません')
  expect(dataLength !== null, 'data チャンクがありません')
  return { ...format, dataLength }
}

function expectCanonical(wav) {
  expect(wav.audioFormat === 1, `PCM ではありません（fmt tag ${wav.audioFormat}）`)
  expect(wav.sampleRate === 48000, `サンプルレートが 48000 ではありません（${wav.sampleRate}）`)
  expect(wav.channels === 1, `モノラルではありません（${wav.channels}ch）`)
  expect(wav.bitDepth === 16, `16bit ではありません（${wav.bitDepth}bit）`)
  expect(wav.dataLength > 0, '音声データが空です')
}

/* ============================================================
 * 4. サーバの起動と停止
 * ============================================================ */

function startServer() {
  return new Promise((resolve, reject) => {
    const child = spawn(exePath, ['--port', '0'], { stdio: ['ignore', 'pipe', 'pipe'] })

    let stdout = ''
    let stderr = ''
    const timer = setTimeout(() => {
      child.kill()
      reject(new Error(`${STARTUP_TIMEOUT_MS} ms 以内に起動しませんでした。\nstderr:\n${stderr}`))
    }, STARTUP_TIMEOUT_MS)

    child.stdout.setEncoding('utf8')
    child.stdout.on('data', (chunk) => {
      stdout += chunk
      const line = stdout.split('\n')[0]
      if (!line.startsWith(PORT_PREFIX)) return

      const port = Number(line.slice(PORT_PREFIX.length).trim())
      if (!Number.isInteger(port) || port <= 0) return

      clearTimeout(timer)
      resolve({ child, port })
    })

    child.stderr.setEncoding('utf8')
    child.stderr.on('data', (chunk) => {
      stderr += chunk
    })

    child.on('error', (error) => {
      clearTimeout(timer)
      reject(error)
    })
    child.on('exit', (code) => {
      clearTimeout(timer)
      reject(new Error(`起動せずに終了しました（exit=${code}）\nstderr:\n${stderr}`))
    })
  })
}

/* ============================================================
 * 5. 検査本体
 * ============================================================ */

const { child, port } = await startServer()
const base = `http://127.0.0.1:${port}`

const speak = (body) =>
  fetch(`${base}/v1/audio/speech`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Accept: 'text/event-stream' },
    body: JSON.stringify(body),
  })

/** 未実装なら NotImplemented を投げる */
async function assertImplemented(response) {
  if (response.status !== 404) return response

  const body = await response.clone().json().catch(() => null)
  if (body?.error?.code === 'NOT_FOUND') throw new NotImplemented()
  return response
}

// ---- 起動 -------------------------------------------------

await check('stdout の 1 行目でポートを申告する', () => {
  expect(Number.isInteger(port) && port > 0, `ポートを読めませんでした（${port}）`)
})

// ---- /health ----------------------------------------------

await check('GET /health が契約どおりの形を返す', async () => {
  const response = await fetch(`${base}/health`)
  expect(response.status === 200, `status=${response.status}`)

  const health = await response.json()
  for (const key of [
    'status', 'model', 'model_loaded', 'gpu',
    'sample_rate', 'channels', 'bit_depth',
    'response_formats', 'stream_formats', 'max_concurrent_synthesis',
  ]) {
    expect(key in health, `${key} がありません`)
  }

  expect(health.sample_rate === 48000, `sample_rate=${health.sample_rate}`)
  expect(health.channels === 1, `channels=${health.channels}`)
  expect(health.bit_depth === 16, `bit_depth=${health.bit_depth}`)
  expect(Array.isArray(health.response_formats), 'response_formats が配列ではありません')
})

await check('/health の gpu を省略していない', async () => {
  // 省略すると nvidia-smi を叩いて PC の GPU を表示するクライアントがある（docs/02 §7）
  const health = await (await fetch(`${base}/health`)).json()
  expect(health.gpu !== null && typeof health.gpu === 'object', 'gpu がありません')
  expect(typeof health.gpu.device_name === 'string' && health.gpu.device_name.length > 0,
    'gpu.device_name が空です')
})

await check('/health の model が非対応の機能を名乗る', async () => {
  // クライアントに落差を伝えられる唯一の口（docs/01 §3）
  const health = await (await fetch(`${base}/health`)).json()
  for (const word of ['参照音声', 'caption', 'seed']) {
    expect(health.model.includes(word), `model 名に「${word}」がありません: ${health.model}`)
  }
})

// ---- 未実装のパス -----------------------------------------

await check('知らないパスは JSON で 404 を返す', async () => {
  const response = await fetch(`${base}/no/such/path`)
  expect(response.status === 404, `status=${response.status}`)

  const body = await response.json()
  expect(body?.error?.code === 'NOT_FOUND', `code=${body?.error?.code}`)
  expect(typeof body?.error?.message === 'string', 'error.message がありません')
})

await check('ドットを含むパスの 404 も JSON で返す', async () => {
  // 引数なしの MapFallback は {*path:nonfile} を使うため、ファイル名に見えるパスに
  // 一致せず、本文 0 バイト・Content-Type なしの 404 になる（docs/08 §4）
  for (const path of ['/openapi.json', '/v1/models.json', '/v1/audio/speech.wav']) {
    const response = await fetch(`${base}${path}`)
    expect(response.status === 404, `${path} status=${response.status}`)

    const body = await response.json().catch(() => null)
    expect(body?.error?.code === 'NOT_FOUND', `${path} の本文が契約の形ではありません`)
  }
})

// ---- /v1/models, /v1/audio/voices --------------------------

await check('GET /v1/models が 200 を返す', () =>
  skipIfNotImplemented(async () => {
    const response = await assertImplemented(await fetch(`${base}/v1/models`))
    expect(response.status === 200, `status=${response.status}`)
  }))

await check('GET /v1/audio/voices が声を並べる', () =>
  skipIfNotImplemented(async () => {
    const response = await assertImplemented(await fetch(`${base}/v1/audio/voices`))
    expect(response.status === 200, `status=${response.status}`)

    const body = await response.json()
    expect(Array.isArray(body?.data) && body.data.length > 0, 'data が空です')
    expect(typeof body.data[0].id === 'string', 'data[].id がありません')
  }))

// ---- /v1/audio/speech --------------------------------------

await check('合成した WAV が 48000Hz / 1ch / 16bit', () =>
  skipIfNotImplemented(async () => {
    const response = await assertImplemented(await speak({ input: 'こんにちは。テストです。' }))
    expect(response.status === 200, `status=${response.status}`)

    const contentType = response.headers.get('content-type') ?? ''
    expect(!contentType.includes('text/event-stream'), `SSE で返っています（${contentType}）`)

    expectCanonical(parseWav(Buffer.from(await response.arrayBuffer())))
  }))

await check('voice: none を弾かない', () =>
  skipIfNotImplemented(async () => {
    // 「指定なし」の意味で none を送る実装がある。弾くと全リクエストが落ちる
    const response = await assertImplemented(await speak({ input: 'テスト', voice: 'none' }))
    expect(response.status === 200, `status=${response.status}`)
  }))

await check('stream_format: sse を要求されても 400 にしない', () =>
  skipIfNotImplemented(async () => {
    const response = await assertImplemented(await speak({ input: 'テスト', stream_format: 'sse' }))
    expect(response.status === 200, `status=${response.status}`)
  }))

await check('非対応のパラメータを黙って受け流す', () =>
  skipIfNotImplemented(async () => {
    // 参照音声や caption でエラーにすると、設定しただけで生成が全件落ちる（docs/01 §3）
    const response = await assertImplemented(await speak({
      input: 'テスト',
      irodori: {
        caption: '明るく元気に',
        seed: 12345,
        num_steps: 32,
        cfg_scale_text: 3,
        ref_wav: 'C:/no/such/reference.wav',
        chunking_enabled: true,
      },
    }))
    expect(response.status === 200, `status=${response.status}`)
  }))

await check('知らない voice は 400 / VOICE_NOT_FOUND', () =>
  skipIfNotImplemented(async () => {
    const response = await assertImplemented(await speak({ input: 'テスト', voice: 'zzz-no-such-voice' }))
    expect(response.status === 400, `status=${response.status}`)

    const body = await response.json()
    expect(body?.error?.code === 'VOICE_NOT_FOUND', `code=${body?.error?.code}`)
    expect(!JSON.stringify(body).includes('reference.wav'), 'エラー本文に参照音声のパスが混ざっています')
  }))

await check('input が空なら 400 / EMPTY_INPUT', () =>
  skipIfNotImplemented(async () => {
    const response = await assertImplemented(await speak({ input: '   ' }))
    expect(response.status === 400, `status=${response.status}`)

    const body = await response.json()
    expect(body?.error?.code === 'EMPTY_INPUT', `code=${body?.error?.code}`)
  }))

await check('wav 以外の response_format は 400 / UNSUPPORTED_FORMAT', () =>
  skipIfNotImplemented(async () => {
    const response = await assertImplemented(await speak({ input: 'テスト', response_format: 'mp3' }))
    expect(response.status === 400, `status=${response.status}`)

    const body = await response.json()
    expect(body?.error?.code === 'UNSUPPORTED_FORMAT', `code=${body?.error?.code}`)
  }))

await check('範囲外の speed は 400 / INVALID_SPEED', () =>
  skipIfNotImplemented(async () => {
    const response = await assertImplemented(await speak({ input: 'テスト', speed: 9 }))
    expect(response.status === 400, `status=${response.status}`)

    const body = await response.json()
    expect(body?.error?.code === 'INVALID_SPEED', `code=${body?.error?.code}`)
  }))

await check('voice はオブジェクトでも受ける', () =>
  skipIfNotImplemented(async () => {
    const voices = await (await fetch(`${base}/v1/audio/voices`)).json()
    const first = voices?.data?.[0]?.id
    expect(typeof first === 'string', '声の一覧を引けません')

    const response = await assertImplemented(await speak({ input: 'テスト', voice: { id: first } }))
    expect(response.status === 200, `status=${response.status}`)
  }))

await check('壊れた JSON は 400 / INVALID_JSON を契約の形で返す', async () => {
  // 素の Minimal API は本文の無い 400 を返す。契約の形に揃っていること
  const response = await fetch(`${base}/v1/audio/speech`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: '{"input": "壊れ',
  })
  expect(response.status === 400, `status=${response.status}`)

  const body = await response.json()
  expect(body?.error?.code === 'INVALID_JSON', `code=${body?.error?.code}`)
  expect(typeof body?.detail === 'string', 'detail がありません')
})

await check('大きすぎる本文は 413 / PAYLOAD_TOO_LARGE を契約の形で返す', async () => {
  const response = await fetch(`${base}/v1/audio/speech`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ input: 'あ'.repeat(3_000_000) }),
  })
  expect(response.status === 413, `status=${response.status}`)

  const body = await response.json()
  expect(body?.error?.code === 'PAYLOAD_TOO_LARGE', `code=${body?.error?.code}`)
})

await check('Authorization ヘッダが付いていても弾かない', () =>
  skipIfNotImplemented(async () => {
    // 認証は実装していないが、付けてくるクライアントがいる（docs/02 §10）
    const response = await assertImplemented(await fetch(`${base}/v1/audio/speech`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', Authorization: 'Bearer dummy' },
      body: JSON.stringify({ input: 'テスト' }),
    }))
    expect(response.status === 200, `status=${response.status}`)
  }))

await check('長文はヘッダを先に返す（チャンク転送）', () =>
  skipIfNotImplemented(async () => {
    // 全部合成してからヘッダを返すと、長文で接続タイムアウトを踏む（docs/02 §4.3）
    const long = 'これはチャンク転送を確かめるための文章です。'.repeat(80)

    const started = Date.now()
    const response = await assertImplemented(await speak({ input: long }))
    const headerMs = Date.now() - started

    expect(response.status === 200, `status=${response.status}`)
    expect(response.headers.get('content-length') === null, 'Content-Length が付いています（分割されていない）')
    expect(headerMs < 3_000, `ヘッダまで ${headerMs} ms かかりました`)

    const wav = parseWav(Buffer.from(await response.arrayBuffer()))
    expectCanonical(wav)
  }))

/* ============================================================
 * 6. 結果
 * ============================================================ */

const failed = results.filter((result) => result.state === 'ng')
const skipped = results.filter((result) => result.state === 'skip')

for (const result of results) {
  const mark = result.state === 'ok' ? 'OK  ' : result.state === 'skip' ? 'SKIP' : 'NG  '
  process.stdout.write(`${mark} ${result.name}${result.note ? `  — ${result.note}` : ''}\n`)
}

process.stdout.write(
  `\nsmoke ${failed.length === 0 ? 'OK' : 'NG'} — ` +
    `合格 ${results.length - failed.length - skipped.length} / ` +
    `未実装 ${skipped.length} / 失敗 ${failed.length}\n`,
)

if (failed.length > 0 && keepAlive) {
  process.stdout.write(`\n--keep が指定されたのでサーバを ${base} で動かしたままにします。Ctrl+C で止めてください。\n`)
} else {
  child.kill()
}

process.exit(failed.length === 0 ? 0 : 1)
