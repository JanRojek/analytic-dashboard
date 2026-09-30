import { useState, type SyntheticEvent } from "react";
import { Alert, Button, TextInput } from "@mantine/core";
import { Link } from "react-router-dom";

import { Icon } from "../../../components/Icon";
import { authApi } from "../../../data/api";
import { AuthLayout } from "../components/AuthLayout";
import { useAuthAction } from "../hooks/useAuthAction";

export default function ForgotPasswordPage() {
    const [email, setEmail] = useState("");

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
            await authApi.forgotPassword(email.trim());

            setMessage(
                "If an account exists for this email, a reset link is on its way.",
            );
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

            <h1>Let’s get you back in.</h1>

            <p className="auth-subtitle">
                Enter your email to receive a password reset link.
            </p>

            {error && (
                <Alert
                    color="red"
                    mb="md"
                    role="alert"
                    title="Unable to continue"
                >
                    {error}
                </Alert>
            )}

            {message && (
                <Alert color="green" mb="md" role="status">
                    {message}
                </Alert>
            )}

            <form className="auth-form" onSubmit={submit}>
                <TextInput
                    label="Email address"
                    placeholder="Enter your email address"
                    value={email}
                    onChange={(e) => setEmail(e.currentTarget.value)}
                    type="email"
                    required
                    autoComplete="email"
                />

                <Button
                    type="submit"
                    color="ink"
                    loading={busy}
                    fullWidth
                    size="md"
                    rightSection={<Icon name="arrow" size={17} />}
                >
                    Send reset link
                </Button>
            </form>

            <Link className="auth-back" to="/login">
                <Icon name="back" size={15} />
                Back to sign in
            </Link>
        </AuthLayout>
    );
}
