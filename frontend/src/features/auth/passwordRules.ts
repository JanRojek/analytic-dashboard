/**
 * Mirrors ASP.NET Core Identity's default password policy so users get instant
 * feedback; the server remains the source of truth.
 */
export type PasswordRule = { id: string; label: string; test: (value: string) => boolean }

export const passwordRules: PasswordRule[] = [
  { id: 'length', label: 'At least 6 characters', test: (v) => v.length >= 6 },
  { id: 'lower', label: 'A lowercase letter', test: (v) => /[a-z]/.test(v) },
  { id: 'upper', label: 'An uppercase letter', test: (v) => /[A-Z]/.test(v) },
  { id: 'digit', label: 'A number', test: (v) => /\d/.test(v) },
  { id: 'symbol', label: 'A symbol', test: (v) => /[^A-Za-z0-9]/.test(v) },
]

export function passwordSatisfiesRules(value: string): boolean {
  return passwordRules.every((rule) => rule.test(value))
}

export function isValidEmail(value: string): boolean {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim())
}
