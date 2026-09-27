import { useState, type SyntheticEvent } from "react";
import { Alert, Button, PasswordInput } from "@mantine/core";
import { Link, useSearchParams } from "react-router-dom";

import { Icon } from "../../../components/Icon";
import { authApi } from "../../../data/api";
import { useSession } from "../../../data/session";
import { AuthLayout } from "../components/AuthLayout";
import { useAuthAction } from "../hooks/useAuthAction";

export default function ResetPasswordPage() {
    const [params] = useSearchParams();
    const { session, clearSession } = useSession();

    const [password, setPassword] = useState("");
    const [confirmPassword, setConfirmPassword] = useState("");

    const {
        busy,
        error,
        message,
        setMessage,
        perform,
    } = useAuthAction();

    function submit(event: SyntheticEvent<HTMLFormElement>) {
        event.preventDefault();

        void perform(async () => {
            if (password !== confirmPassword) {
                throw new Error("The passwords do not match.");
            }

            if (
                !/(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).{6,}/.test(
                    password,
                )
            ) {
                throw new Error(
                    "Use at least 6 characters, with an uppercase letter, a lowercase letter, a number, and a symbol.",
                );
            }

            const userId = params.get("userId");
            const token = params.get("token");

            if (!userId || !token) {
                throw new Error(
                    "This reset link is incomplete. Request a new one from the sign-in page.",
                );
            }

            await authApi.resetPassword(userId, token, password);

            if (session?.mode === "api") {
                clearSession();
            }

            setMessage("Your password has been updated. You can now sign in.");
            setPassword("");
            setConfirmPassword("");
        });
    }

    return (
        <AuthLayout
            topAction={
                <>
                    <span>New around here?</span>

                    <Link to="/register">
                        Create an account <Icon name="arrow" size={15} />
                    </Link>
                </>
            }
        >
            <div className="eyebrow">YOUR ANALYTICAL WORKSPACE</div>

            <h1>A fresh start.</h1>

            <p className="auth-subtitle">
                Choose a new password for your account.
            </p>

            {error && (
                <Alert color="red" mb="md" role="alert" title="Unable to continue">
                    {error}
                </Alert>
            )}

            {message && (
                <Alert color="green" mb="md" role="status">
                    {message}
                </Alert>
            )}

            <form className="auth-form" onSubmit={submit}>
                <PasswordInput
                    label="Password"
                    placeholder="Create a strong password"
                    value={password}
                    onChange={(e) => setPassword(e.currentTarget.value)}
                    required
                    autoComplete="new-password"
                    description="6+ characters with uppercase, lowercase, a number, and a symbol."
                />

                <PasswordInput
                    label="Confirm password"
                    placeholder="Enter your password again"
                    value={confirmPassword}
                    onChange={(e) => setConfirmPassword(e.currentTarget.value)}
                    required
                    autoComplete="new-password"
                />

                <Button
                    type="submit"
                    loading={busy}
                    fullWidth
                    size="md"
                    rightSection={<Icon name="arrow" size={17} />}
                >
                    Update password
                </Button>
            </form>

            <Link className="auth-back" to="/login">
                <Icon name="back" size={15} /> Back to sign in
            </Link>
        </AuthLayout>
    );
}
