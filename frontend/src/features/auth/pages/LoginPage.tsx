import { useState, type SyntheticEvent } from "react";
import {
    Alert,
    Button,
    Checkbox,
    Divider,
    PasswordInput,
    TextInput,
} from "@mantine/core";
import { Link, useNavigate } from "react-router-dom";

import { Icon } from "../../../components/Icon";
import { ApiError, authApi } from "../../../data/api";
import { useSession } from "../../../data/session";
import { AuthLayout } from "../components/AuthLayout";
import { useAuthAction } from "../hooks/useAuthAction";

export default function LoginPage() {
    const navigate = useNavigate();
    const { signIn, startDemo } = useSession();

    const [email, setEmail] = useState("");
    const [password, setPassword] = useState("");
    const [remember, setRemember] = useState(true);
    const [needsConfirmation, setNeedsConfirmation] = useState(false);

    const {
        busy,
        error,
        message,
        setError,
        setMessage,
        perform,
    } = useAuthAction();

    function submit(event: SyntheticEvent<HTMLFormElement>) {
        event.preventDefault();

        void perform(async () => {
            setNeedsConfirmation(false);

            try {
                await authApi.login(email.trim(), password, remember);
            } catch (failure) {
                if (failure instanceof ApiError && failure.status === 409) {
                    setNeedsConfirmation(true);
                }

                throw failure;
            }

            signIn(await authApi.me());
            navigate("/projects");
        });
    }

    function demo() {
        try {
            startDemo();
            navigate("/projects");
        } catch {
            setError(
                "Browser storage is unavailable. Allow local storage to open the demo.",
            );
        }
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

            <h1>Welcome back.</h1>

            <p className="auth-subtitle">
                Sign in and pick up where curiosity left off.
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
                <TextInput
                    label="Email address"
                    placeholder="you@example.com"
                    value={email}
                    onChange={(e) => setEmail(e.currentTarget.value)}
                    type="email"
                    required
                    autoComplete="email"
                />

                <PasswordInput
                    label="Password"
                    placeholder="Enter your password"
                    value={password}
                    onChange={(e) => setPassword(e.currentTarget.value)}
                    required
                    autoComplete="current-password"
                />

                <div className="auth-options">
                    <Checkbox
                        label="Keep me signed in"
                        checked={remember}
                        onChange={(e) => setRemember(e.currentTarget.checked)}
                        size="sm"
                    />

                    <Link to="/forgot-password">Forgot password?</Link>
                </div>

                <Button
                    type="submit"
                    loading={busy}
                    fullWidth
                    size="md"
                    rightSection={<Icon name="arrow" size={17} />}
                >
                    Sign in
                </Button>
            </form>

            {needsConfirmation && (
                <Button
                    fullWidth
                    variant="subtle"
                    mt="md"
                    loading={busy}
                    onClick={() =>
                        void perform(async () => {
                            await authApi.resendConfirmation(email.trim());
                            setMessage("A new confirmation email has been requested.");
                        })
                    }
                >
                    Resend confirmation email
                </Button>
            )}

            <Divider
                label="or take a look around"
                labelPosition="center"
                my={24}
            />

            <Button
                className="demo-button"
                variant="default"
                fullWidth
                size="md"
                disabled={busy}
                onClick={demo}
                leftSection={<Icon name="play" size={15} />}
            >
                Explore the demo workspace
            </Button>

            <p className="demo-caption">
                Sample data. No account needed. Your changes stay in this browser.
            </p>
        </AuthLayout>
    );
}
