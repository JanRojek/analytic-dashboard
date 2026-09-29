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
                            placeholder="you@example.com"
                            value={email}
                            onChange={(e) => setEmail(e.currentTarget.value)}
                            type="email"
                            required
                            autoComplete="email"
                        />

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
                            onChange={(e) =>
                                setConfirmPassword(e.currentTarget.value)
                            }
                            required
                            autoComplete="new-password"
                        />

                        <Button
                            type="submit"
                            loading={busy}
                            fullWidth
                            size="md"
                            rightSection={
                                <Icon name="arrow" size={17} />
                            }
                        >
                            Create account
                        </Button>
                    </form>

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
