/** Keeps the email of an in-progress registration so the confirmation screen can show it. */
const KEY = 'cairn.registration.email'

export function rememberRegistrationEmail(email: string): void {
  try {
    window.sessionStorage.setItem(KEY, email)
  } catch {
    // ignore
  }
}

export function readRegistrationEmail(): string | null {
  try {
    return window.sessionStorage.getItem(KEY)
  } catch {
    return null
  }
}

export function clearRegistrationEmail(): void {
  try {
    window.sessionStorage.removeItem(KEY)
  } catch {
    // ignore
  }
}
