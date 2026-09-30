import { useState, type SyntheticEvent } from "react";
import
{
    Alert,
    Button,
    Divider,
    PasswordInput,
    TextInput,
} from "@mantine/core";
import { Link, useNavigate } from "react-router-dom";

import { Icon } from "../../../components/Icon";
import { authApi } from "../../../data/api";
import { useSession } from "../../../session/SessionContext";
import { AuthLayout } from "../components/AuthLayout";
import { RegistrationPending } from "../components/RegistrationPending";
import { useAuthAction } from "../hooks/useAuthAction";

export default function RegisterPage()
{
    const navigate = useNavigate();
    const { startDemo } = useSession();

    const [name, setName] = useState("");
    const [email, setEmail] = useState("");
    const [password, setPassword] = useState("");
    const [confirmPassword, setConfirmPassword] = useState("");
    const [registered, setRegistered] = useState(false);
    const [googleNotice, setGoogleNotice] = useState(false);

    const {
        busy,
        error,
        setError,
        perform,
    } = useAuthAction();

    function submit(event: SyntheticEvent<HTMLFormElement>)
    {
        event.preventDefault();

        void perform(async () =>
        {
            if (password !== confirmPassword)
            {
                throw new Error("The passwords do not match.");
            }

            if (!/(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).{6,}/.test(password))
            {
                throw new Error(
                    "Use at least 6 characters, with an uppercase letter, a lowercase letter, a number, and a symbol.",
                );
            }

            await authApi.register(
                name.trim(),
                email.trim(),
                password,
            );

            setRegistered(true);
            setPassword("");
            setConfirmPassword("");
        });
    }

    function demo()
    {
        try
        {
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
                    <span>Already have an account?</span>

                    <Link to="/login">
                        Sign in <Icon name="arrow" size={15} />
                    </Link>
                </>
            }
        >
            {registered ? (
                <RegistrationPending email={email} />
            ) : (
                <>
                    <div className="eyebrow">
                        YOUR ANALYTICAL WORKSPACE
                    </div>

                    <h1>Room for your next idea.</h1>

                    <p className="auth-subtitle">
                        Create an account. Give your data a new perspective.
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

                    {googleNotice && (
                        <Alert color="gray" mb="md" role="status">
                            Google sign-up is not enabled yet.
                        </Alert>
                    )}

                    <form className="auth-form" onSubmit={submit}>
                        <TextInput
                            label="Full name"
                            placeholder="Your name"
                            value={name}
                            onChange={(e) => setName(e.currentTarget.value)}
                            required
                            maxLength={100}
                            autoComplete="name"
                        />

                        <TextInput
                            label="Email address"
                            placeholder="Enter your email address"
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
                            autoComplete="new-password"
                            description="6+ characters with uppercase, lowercase, a number, and a symbol."
                        />

                        <PasswordInput
                            label="Confirm password"
                            placeholder="Enter your password again"
                            value={confirmPassword}
                            onChange={(e) =>
                                setConfirmPassword(e.currentTarget.value)
                            }
                            required
                            autoComplete="new-password"
                        />

                        <Button
                            type="submit"
                            color="ink"
                            loading={busy}
                            fullWidth
                            size="md"
                            rightSection={<Icon name="arrow" size={17} />}
                        >
                            Create account
                        </Button>

                        <Button
                            type="button"
                            className="google-button"
                            variant="default"
                            fullWidth
                            size="md"
                            disabled={busy}
                            leftSection={<GoogleLogo />}
                            onClick={() => setGoogleNotice(true)}
                        >
                            Continue with Google
                        </Button>
                    </form>

                    <Divider
                        label="or take a look around"
                        labelPosition="center"
                        my={16}
                    />

                    <Button
                        className="demo-button"
                        variant="default"
                        fullWidth
                        size="md"
                        disabled={busy}
                        onClick={demo}
                        leftSection={
                            <Icon name="play" size={15} />
                        }
                    >
                        Explore the demo workspace
                    </Button>

                    <p className="demo-caption">
                        Sample data. No account needed. Your changes stay in this
                        browser.
                    </p>
                </>
            )}
        </AuthLayout>
    );
}

function GoogleLogo() {
    return (
        <svg
            width="18"
            height="18"
            viewBox="0 0 18 18"
            aria-hidden="true"
        >
            <path
                fill="#4285F4"
                d="M17.64 9.205c0-.639-.057-1.252-.164-1.841H9v3.482h4.844a4.14 4.14 0 0 1-1.797 2.716v2.258h2.909c1.702-1.567 2.684-3.874 2.684-6.615Z"
            />
            <path
                fill="#34A853"
                d="M9 18c2.43 0 4.468-.806 5.956-2.18l-2.909-2.258c-.806.54-1.836.859-3.047.859-2.345 0-4.33-1.585-5.04-3.714H.952v2.332A9 9 0 0 0 9 18Z"
            />
            <path
                fill="#FBBC05"
                d="M3.96 12.707A5.42 5.42 0 0 1 3.682 11c0-.593.102-1.168.278-1.707V6.961H.952A9 9 0 0 0 0 11c0 1.452.347 2.827.952 4.039l3.008-2.332Z"
            />
            <path
                fill="#EA4335"
                d="M9 3.58c1.322 0 2.508.455 3.441 1.346l2.581-2.581C13.464.891 11.427 0 9 0A9 9 0 0 0 .952 6.961L3.96 9.293C4.67 7.165 6.655 3.58 9 3.58Z"
            />
        </svg>
    );
}
