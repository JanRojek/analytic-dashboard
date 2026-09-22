/**
 * Lightweight CSV sniffing for the import preview. This runs entirely in the
 * browser on the first chunk of the file; the authoritative parse happens on the
 * server (DuckDB), so the goal here is a faithful preview, not a full parser.
 */

export type CsvPreview = {
  delimiter: string
  headers: string[]
  rows: string[][]
  /** Rough estimate based on the average line length in the sampled chunk. */
  estimatedRowCount: number
  sampledBytes: number
  truncated: boolean
}

const CANDIDATE_DELIMITERS = [',', ';', '\t', '|']

export function detectDelimiter(sample: string): string {
  const lines = sample.split(/\r?\n/).filter((line) => line.trim().length > 0).slice(0, 20)
  let best = ','
  let bestScore = -1
  for (const delimiter of CANDIDATE_DELIMITERS) {
    const counts = lines.map((line) => splitLine(line, delimiter).length)
    if (counts.length === 0) continue
    const first = counts[0]
    if (first <= 1) continue
    const consistent = counts.filter((count) => count === first).length
    const score = consistent * 10 + first
    if (score > bestScore) {
      bestScore = score
      best = delimiter
    }
  }
  return best
}

export function splitLine(line: string, delimiter: string): string[] {
  const cells: string[] = []
  let current = ''
  let inQuotes = false
  for (let i = 0; i < line.length; i++) {
    const char = line[i]
    if (inQuotes) {
      if (char === '"') {
        if (line[i + 1] === '"') {
          current += '"'
          i++
        } else {
          inQuotes = false
        }
      } else {
        current += char
      }
    } else if (char === '"') {
      inQuotes = true
    } else if (char === delimiter) {
      cells.push(current)
      current = ''
    } else {
      current += char
    }
  }
  cells.push(current)
  return cells.map((cell) => cell.trim())
}

export async function previewCsv(file: File, maxRows = 8, sampleBytes = 64 * 1024): Promise<CsvPreview> {
  const slice = file.slice(0, sampleBytes)
  const text = await slice.text()
  const truncated = file.size > sampleBytes
  const rawLines = text.split(/\r?\n/)
  // Drop a possibly cut-off last line when the sample is truncated.
  const lines = (truncated ? rawLines.slice(0, -1) : rawLines).filter((line) => line.length > 0)

  const delimiter = detectDelimiter(lines.slice(0, 20).join('\n'))
  const headers = lines.length > 0 ? splitLine(lines[0], delimiter) : []
  const rows = lines.slice(1, 1 + maxRows).map((line) => {
    const cells = splitLine(line, delimiter)
    return headers.map((_, index) => cells[index] ?? '')
  })

  const dataLines = Math.max(lines.length - 1, 0)
  const bytesForData = Math.max(text.length - (lines[0]?.length ?? 0) - 1, 1)
  const averageLine = dataLines > 0 ? bytesForData / dataLines : 0
  const estimatedRowCount =
    truncated && averageLine > 0
      ? Math.round((file.size - (lines[0]?.length ?? 0)) / averageLine)
      : dataLines

  return {
    delimiter,
    headers,
    rows,
    estimatedRowCount,
    sampledBytes: Math.min(file.size, sampleBytes),
    truncated,
  }
}

export function delimiterLabel(delimiter: string): string {
  switch (delimiter) {
    case ',':
      return 'comma'
    case ';':
      return 'semicolon'
    case '\t':
      return 'tab'
    case '|':
      return 'pipe'
    default:
      return JSON.stringify(delimiter)
  }
}
