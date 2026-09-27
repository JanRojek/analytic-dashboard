const REGISTRATION_CHANNEL_NAME = "aperture.registration.v1";
const EMAIL_CONFIRMED_EVENT = "email-confirmed";

export function notifyEmailConfirmed() {
    if (typeof BroadcastChannel === "undefined") {
        return;
    }

    const channel = new BroadcastChannel(REGISTRATION_CHANNEL_NAME);

    channel.postMessage(EMAIL_CONFIRMED_EVENT);
    channel.close();
}

export function subscribeToEmailConfirmed(
    onConfirmed: () => void,
) {
    if (typeof BroadcastChannel === "undefined") {
        return () => {};
    }

    const channel = new BroadcastChannel(REGISTRATION_CHANNEL_NAME);

    function handleMessage(event: MessageEvent) {
        if (event.data === EMAIL_CONFIRMED_EVENT) {
            onConfirmed();
        }
    }

    channel.addEventListener("message", handleMessage);

    return () => {
        channel.removeEventListener("message", handleMessage);
        channel.close();
    };
}
