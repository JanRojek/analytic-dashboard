import { Alert, Button } from "@mantine/core";
import { Link, useSearchParams } from "react-router-dom";

import { Icon } from "../../../components/Icon";
import { authApi } from "../../../data/api";
import { AuthLayout } from "../components/AuthLayout";
import { useAuthAction } from "../hooks/useAuthAction";
import { notifyEmailConfirmed } from "../registrationEvents";

export default function ConfirmEmailPage() {
    const [params] = useSearchParams();

    const {
        busy,
        error,
        message,
        setMessage,
        perform,
    } = useAuthAction();

    function confirmEmail() {
        void perform(async () => {
            const userId = params.get("userId");
            const token = params.get("token");

            if (!userId || !token) {
                throw new Error(
                    "This confirmation link is incomplete. Request another confirmation email.",
                );
            }

            await authApi.confirmEmail(userId, token);

            notifyEmailConfirmed();

            setMessage(
                "Your email is confirmed. You can close this tab.",
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

            <h1>One last step.</h1>

            <p className="auth-subtitle">
                Confirm your email to start exploring your data.
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

            {!message && (
                <div className="auth-form">
                    <Button
                        loading={busy}
                        fullWidth
                        size="md"
                        rightSection={<Icon name="arrow" size={17} />}
                        onClick={confirmEmail}
                    >
                        Confirm email
                    </Button>
                </div>
            )}

            <Link className="auth-back" to="/login">
                <Icon name="back" size={15} /> Back to sign in
            </Link>
        </AuthLayout>
    );
}
