import { useState } from "react";

export function useAuthAction() {
    const [busy, setBusy] = useState(false);
    const [error, setError] = useState("");
    const [message, setMessage] = useState("");

    async function perform(action: () => Promise<void>) {
        setBusy(true);
        setError("");
        setMessage("");

        try {
            await action();
        } catch (e) {
            setError(e instanceof Error ? e.message : "Please try again.");
        } finally {
            setBusy(false);
        }
    }

    return {
        busy,
        error,
        message,
        setError,
        setMessage,
        perform,
    };
}
