#!/usr/bin/env node
/**
 * docs/02-http-contract.md §6.2 のエラーコード表と、Contract/ErrorCodes.cs を突き合わせる。
 *
 * なぜ検査にしてあるか:
 *   「設計の正は docs にある」は、文章で約束しても守られない。片方だけ直せる状態だと、
 *   必ず片方だけ直る。表と実装のどちらかが欠けたらビルド前に落ちるようにしておく。
 *
 * 何を見るか:
 *   1. 表にあるコードが ErrorCodes.cs に無い       → NG
 *   2. ErrorCodes.cs にあるコードが表に無い         → NG
 *   3. 表の HTTP ステータスが 3 桁の数値でない      → NG
 *   4. 表から拾えたコードが MIN_EXPECTED 未満       → NG
 *      （表の書式が壊れて 1 行しか読めない状態を「全件一致」と誤判定しないため）
 *
 * 依存ゼロ。node:fs だけを使う。
 */

import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { dirname, join } from 'node:path'

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..')
const DOC = join(ROOT, 'docs', '02-http-contract.md')
const SOURCE = join(ROOT, 'src', 'OpenAiWindowsTts', 'Contract', 'ErrorCodes.cs')

const BEGIN = '<!-- check-contract:error-codes:begin -->'
const END = '<!-- check-contract:error-codes:end -->'

/** 表が壊れて数行しか読めない状態を「全件一致」と誤判定しないための下限 */
const MIN_EXPECTED = 5

const problems = []

function read(path, label) {
  try {
    return readFileSync(path, 'utf8')
  } catch (error) {
    problems.push(`${label} を読めません: ${path}\n  ${error.message}`)
    return null
  }
}

/** docs/02 のマーカーに挟まれた表から code と HTTP ステータスを拾う */
function parseDoc(text) {
  const begin = text.indexOf(BEGIN)
  const end = text.indexOf(END)
  if (begin < 0 || end < 0 || end < begin) {
    problems.push(
      `docs/02-http-contract.md にマーカーが見つかりません。\n` +
        `  ${BEGIN} と ${END} で表を挟んでください。`,
    )
    return []
  }

  const rows = []
  for (const line of text.slice(begin + BEGIN.length, end).split('\n')) {
    const trimmed = line.trim()
    if (!trimmed.startsWith('|')) continue

    const cells = trimmed.split('|').slice(1, -1).map((cell) => cell.trim())
    if (cells.length < 2) continue

    // ヘッダ行と区切り行（| --- | --- |）を飛ばす
    const code = /^`([A-Z][A-Z0-9_]*)`$/.exec(cells[0])
    if (code === null) continue

    if (!/^\d{3}$/.test(cells[1])) {
      problems.push(`表の HTTP ステータスが 3 桁の数値ではありません: ${code[1]} → "${cells[1]}"`)
    }

    rows.push(code[1])
  }
  return rows
}

/** ErrorCodes.cs から public const string の値を拾う */
function parseSource(text) {
  const found = []
  const pattern = /public\s+const\s+string\s+\w+\s*=\s*"([^"]*)"\s*;/g
  let match
  while ((match = pattern.exec(text)) !== null) {
    found.push(match[1])
  }
  return found
}

function duplicates(values) {
  const seen = new Set()
  const dupes = new Set()
  for (const value of values) {
    if (seen.has(value)) dupes.add(value)
    seen.add(value)
  }
  return [...dupes]
}

const docText = read(DOC, 'docs/02-http-contract.md')
const sourceText = read(SOURCE, 'Contract/ErrorCodes.cs')

if (docText !== null && sourceText !== null) {
  const inDoc = parseDoc(docText)
  const inSource = parseSource(sourceText)

  for (const dupe of duplicates(inDoc)) problems.push(`表にコードが重複しています: ${dupe}`)
  for (const dupe of duplicates(inSource)) problems.push(`ErrorCodes.cs にコードが重複しています: ${dupe}`)

  if (inDoc.length < MIN_EXPECTED) {
    problems.push(
      `表から拾えたコードが ${inDoc.length} 件しかありません（下限 ${MIN_EXPECTED}）。\n` +
        `  表の書式が壊れている可能性があります。1 列目は \`CODE\` の形にしてください。`,
    )
  }

  const docSet = new Set(inDoc)
  const sourceSet = new Set(inSource)

  const missingInSource = inDoc.filter((code) => !sourceSet.has(code))
  const missingInDoc = inSource.filter((code) => !docSet.has(code))

  if (missingInSource.length > 0) {
    problems.push(
      `表にあるのに ErrorCodes.cs に無いコード: ${missingInSource.join(', ')}\n` +
        `  src/OpenAiWindowsTts/Contract/ErrorCodes.cs に const を足してください。`,
    )
  }
  if (missingInDoc.length > 0) {
    problems.push(
      `ErrorCodes.cs にあるのに表に無いコード: ${missingInDoc.join(', ')}\n` +
        `  docs/02-http-contract.md §6.2 の表に行を足してください。`,
    )
  }

  if (problems.length === 0) {
    process.stdout.write(`check-contract OK — エラーコード ${inDoc.length} 件が表と実装で一致\n`)
    process.exit(0)
  }
}

process.stderr.write('check-contract NG\n\n')
for (const problem of problems) {
  process.stderr.write(`  - ${problem}\n`)
}
process.stderr.write('\n')
process.exit(1)
