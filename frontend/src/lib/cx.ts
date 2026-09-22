/** Tiny class-name joiner (avoids a dependency for a one-liner). */
export function cx(...values: Array<string | false | null | undefined>): string {
  return values.filter(Boolean).join(' ')
}
