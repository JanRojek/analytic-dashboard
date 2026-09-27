import { useEffect, useRef, useState } from "react";
import { Alert, Button } from "@mantine/core";
import { useNavigate } from "react-router-dom";

import { Icon } from "../../../components/Icon";
import { authApi } from "../../../data/api";
import { useSession } from "../../../data/session";
import { useAuthAction } from "../hooks/useAuthAction";
import { subscribeToEmailConfirmed } from "../registrationEvents";

const POLL_INTERVAL_MS = 3000;

type RegistrationPendingProps = {
    email: string;
};

export function RegistrationPending({
    email,
}: RegistrationPendingProps) {
    const navigate = useNavigate();
    const { signIn } = useSession();

    const completingRef = useRef(false);

    const [pollError, setPollError] = useState("");
    const [completing, setCompleting] = useState(false);

    const {
        busy,
        error,
        message,
        setMessage,
        perform,
    } = useAuthAction();

    useEffect(() => {
        let cancelled = false;

        async function checkRegistration() {
            try {
                const status = await authApi.registrationStatus();

                if (cancelled)
                {
                    return;
                }

                setPollError("");

                if (status.status !== "Confirmed" || completingRef.current)
                {
                    return;
                }

                completingRef.current = true;
                setCompleting(true);

                try {
                    await authApi.completeRegistration();

                    if (cancelled) {
                        return;
                    }

                    const user = await authApi.me();

                    if (cancelled) {
                        return;
                    }

                    signIn(user);
                    navigate("/projects");
                } catch (e) {
                    if (cancelled) {
                        return;
                    }

                    completingRef.current = false;
                    setCompleting(false);
                    setPollError(
                        e instanceof Error
                            ? e.message
                            : "Unable to complete registration.",
                    );
                }
            } catch (e) {
                if (cancelled) {
                    return;
                }

                setPollError(
                    e instanceof Error
                        ? e.message
                        : "Unable to check confirmation status.",
                );
            }
        }

        void checkRegistration();

        const interval = window.setInterval(
            () => void checkRegistration(),
            POLL_INTERVAL_MS,
        );

        const unsubscribe = subscribeToEmailConfirmed(() => {
            void checkRegistration();
        });

        return () => {
            cancelled = true;
            window.clearInterval(interval);
            unsubscribe();
        };
    }, [navigate, signIn]);

    return (
        <>
            <div className="eyebrow">
                YOUR ANALYTICAL WORKSPACE
            </div>

            <h1>Check your inbox.</h1>

            <p className="auth-subtitle">
                We sent a confirmation link to {email}. Confirm your email and
                we’ll continue automatically.
            </p>

            {(error || pollError) && (
                <Alert
                    color="red"
                    mb="md"
                    role="alert"
                    title="Unable to continue"
                >
                    {error || pollError}
                </Alert>
            )}

            {message && (
                <Alert color="green" mb="md" role="status">
                    {message}
                </Alert>
            )}

            <div className="auth-form">
                <div className="confirmation-symbol">
                    <Icon name="mail" size={30} />
                </div>

                <p className="demo-caption" aria-live="polite">
                    {completing
                        ? "Finishing your registration…"
                        : "Waiting for email confirmation…"}
                </p>

                <Button
                    variant="subtle"
                    loading={busy}
                    disabled={completing}
                    onClick={() =>
                        void perform(async () => {
                            await authApi.resendConfirmation(email);
                            setMessage(
                                "A new confirmation email has been requested.",
                            );
                        })
                    }
                >
                    Resend confirmation email
                </Button>
            </div>
        </>
    );
}
