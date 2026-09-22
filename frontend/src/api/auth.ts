import { request } from './client'
import type {
  CurrentUser,
  LoginUserRequest,
  RegisterUserRequest,
  RegisterUserResponse,
  RegistrationStatus,
} from './types'

export const authApi = {
  me: (signal?: AbortSignal) =>
    request<CurrentUser>('/auth/me', { signal, silentUnauthorized: true }),

  login: (body: LoginUserRequest) =>
    request<void>('/auth/login', { method: 'POST', body, silentUnauthorized: true }),

  logout: () => request<void>('/auth/logout', { method: 'POST', silentUnauthorized: true }),

  register: (body: RegisterUserRequest) =>
    request<RegisterUserResponse>('/auth/register', { method: 'POST', body }),

  registrationStatus: (signal?: AbortSignal) =>
    request<{ status: RegistrationStatus }>('/auth/registration-status', {
      signal,
      silentUnauthorized: true,
    }),

  completeRegistration: () =>
    request<void>('/auth/complete-registration', { method: 'POST', silentUnauthorized: true }),

  confirmEmail: (body: { userId: string; token: string }) =>
    request<void>('/auth/confirm-email', { method: 'POST', body }),

  resendConfirmation: (email: string) =>
    request<void>('/auth/resend-confirmation', { method: 'POST', body: { email } }),

  forgotPassword: (email: string) =>
    request<void>('/auth/forgot-password', { method: 'POST', body: { email } }),

  resetPassword: (body: { userId: string; token: string; newPassword: string }) =>
    request<void>('/auth/reset-password', { method: 'POST', body }),
}
