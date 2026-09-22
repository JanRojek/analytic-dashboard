import { createBrowserRouter, Navigate } from "react-router-dom";

import { AuthLayout } from "../features/auth/AuthLayout";
import { AuthProvider } from "../features/auth/AuthProvider";

import { SignInPage } from "../features/auth/SignInPage";
import { RegisterPage } from "../features/auth/RegisterPage";
import { RegisterConfirmPage } from "../features/auth/RegisterConfirmPage";
import { ConfirmEmailPage } from "../features/auth/ConfirmEmailPage";
import {
    ForgotPasswordPage,
    ResetPasswordPage,
} from "../features/auth/PasswordPages";

export const router = createBrowserRouter([
    {
        element: (
            <AuthProvider>
                <AuthLayout />
            </AuthProvider>
        ),
        children: [
            {
                path: "/",
                element: <Navigate to="/sign-in" replace />,
            },
            {
                path: "/sign-in",
                element: <SignInPage />,
            },
            {
                path: "/login",
                element: <Navigate to="/sign-in" replace />,
            },
            {
                path: "/register",
                element: <RegisterPage />,
            },
            {
                path: "/register/confirm",
                element: <RegisterConfirmPage />,
            },
            {
                path: "/confirm-email",
                element: <ConfirmEmailPage />,
            },
            {
                path: "/forgot-password",
                element: <ForgotPasswordPage />,
            },
            {
                path: "/reset-password",
                element: <ResetPasswordPage />,
            },
        ],
    },
]);
