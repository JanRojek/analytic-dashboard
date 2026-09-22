import { useState, type FormEvent } from "react";
import {
  Link,
  Navigate,
  useLocation,
  useNavigate,
  useSearchParams,
} from "react-router-dom";
import {
  Alert,
  Button,
  Checkbox,
  Divider,
  PasswordInput,
  TextInput,
} from "@mantine/core";
import { Brand } from "../components/Brand";
import { Icon } from "../components/Icon";
import { ApiError, authApi } from "../data/api";
import { useSession } from "../data/session";

function AuthArtwork() {
  return (
    <div className="auth-art" aria-hidden="true">
      <div className="orbit orbit-one" />
      <div className="orbit orbit-two" />
      <div className="art-paper art-paper-back" />
      <div className="art-paper">
        <div className="art-label">
          <span className="tiny-dot" /> A NEW PERSPECTIVE
        </div>
        <div className="art-number">Ideas into insight.</div>
        <div className="art-chart">
          <svg viewBox="0 0 350 135" fill="none">
            <path
              d="M0 35H350M0 75H350M0 115H350"
              stroke="#e2e8df"
              strokeDasharray="3 5"
            />
            <path
              d="M0 110 35 98 70 106 105 78 140 87 175 55 210 61 245 25 280 38 315 12 350 4V135H0Z"
              fill="#dfebdc"
            />
            <path
              d="M0 110 35 98 70 106 105 78 140 87 175 55 210 61 245 25 280 38 315 12 350 4"
              stroke="#427555"
              strokeWidth="2.5"
            />
          </svg>
        </div>
        <div className="art-axis">
          <span>YOUR DATA</span>
          <span>YOUR NEXT CHAPTER</span>
        </div>
      </div>
      <div className="art-floating">
        <span className="art-check">
          <Icon name="check" size={16} />
        </span>
        <div>
          <strong>Make the connection</strong>
          <span>From the first row to the bigger picture.</span>
        </div>
      </div>
    </div>
  );
}

export default function AuthPage() {
  const location = useLocation();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const { session, signIn, startDemo, clearSession } = useSession();
  const mode =
    location.pathname === "/register"
      ? "register"
      : location.pathname === "/forgot-password"
        ? "forgot"
        : location.pathname === "/reset-password"
          ? "reset"
          : location.pathname === "/confirm-email"
            ? "confirm"
            : "login";
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [remember, setRemember] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [registered, setRegistered] = useState(false);
  const [needsConfirmation, setNeedsConfirmation] = useState(false);
  if (session && mode !== "confirm" && mode !== "reset")
    return <Navigate to="/projects" replace />;

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
  function submit(event: FormEvent) {
    event.preventDefault();
    void perform(async () => {
      if (mode === "register" || mode === "reset") {
        if (password !== confirmPassword)
          throw new Error("The passwords do not match.");
        if (
          !/(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).{6,}/.test(
            password,
          )
        )
          throw new Error(
            "Use at least 6 characters, with an uppercase letter, a lowercase letter, a number, and a symbol.",
          );
      }
      if (mode === "login") {
        setNeedsConfirmation(false);
        try {
          await authApi.login(email.trim(), password, remember);
        } catch (failure) {
          if (failure instanceof ApiError && failure.status === 409)
            setNeedsConfirmation(true);
          throw failure;
        }
        signIn(await authApi.me());
        navigate("/projects");
      } else if (mode === "register") {
        await authApi.register(name.trim(), email.trim(), password);
        setRegistered(true);
        setPassword("");
        setConfirmPassword("");
      } else if (mode === "forgot") {
        await authApi.forgotPassword(email.trim());
        setMessage(
          "If an account exists for this email, a reset link is on its way.",
        );
      } else if (mode === "reset") {
        const userId = params.get("userId"),
          token = params.get("token");
        if (!userId || !token)
          throw new Error(
            "This reset link is incomplete. Request a new one from the sign-in page.",
          );
        await authApi.resetPassword(userId, token, password);
        if (session?.mode === "api") clearSession();
        setMessage("Your password has been updated. You can now sign in.");
        setPassword("");
        setConfirmPassword("");
      } else {
        const userId = params.get("userId"),
          token = params.get("token");
        if (!userId || !token)
          throw new Error(
            "This confirmation link is incomplete. Request another confirmation email.",
          );
        await authApi.confirmEmail(userId, token);
        setMessage("Your email is confirmed. You can now sign in.");
      }
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
  const title = registered
    ? "Check your inbox."
    : {
        login: "Welcome back.",
        register: "Room for your next idea.",
        forgot: "Let’s get you back in.",
        reset: "A fresh start.",
        confirm: "One last step.",
      }[mode];
  const subtitle = registered
    ? `We sent a confirmation link to ${email}. Confirm your email, then continue below.`
    : {
        login: "Sign in and pick up where curiosity left off.",
        register: "Create an account. Give your data a new perspective.",
        forgot: "Enter your email to receive a password reset link.",
        reset: "Choose a new password for your account.",
        confirm: "Confirm your email to start exploring your data.",
      }[mode];
  return (
    <div className="auth-layout">
      <aside className="auth-story">
        <Brand />
        <div className="auth-story-body">
          <div className="eyebrow">A LITTLE CLARITY GOES A LONG WAY</div>
          <div className="auth-story-title">
            Your data.
            <br />A clearer perspective.
          </div>
          <p>
            Bring the pieces together. Find the story in your data, and make it
            something worth sharing.
          </p>
          <AuthArtwork />
        </div>
        <div className="auth-story-footer">
          <span>Collect. Understand. Create.</span>
          <span>Made for the curious.</span>
        </div>
      </aside>
      <main className="auth-main">
        <div className="auth-top">
          <span>
            {mode === "register"
              ? "Already have an account?"
              : "New around here?"}
          </span>
          <Link to={mode === "register" ? "/login" : "/register"}>
            {mode === "register" ? "Sign in" : "Create an account"}{" "}
            <Icon name="arrow" size={15} />
          </Link>
        </div>
        <div className="auth-form-wrap">
          <div className="mobile-brand">
            <Brand />
          </div>
          <div className="eyebrow">YOUR ANALYTICAL WORKSPACE</div>
          <h1>{title}</h1>
          <p className="auth-subtitle">{subtitle}</p>
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
          {registered ? (
            <div className="auth-form">
              <div className="confirmation-symbol">
                <Icon name="mail" size={30} />
              </div>
              <Button
                loading={busy}
                onClick={() =>
                  void perform(async () => {
                    const status = await authApi.registrationStatus();
                    if (status.status !== "Confirmed")
                      throw new Error(
                        "Your email is not confirmed yet. Open the link in your inbox first.",
                      );
                    await authApi.completeRegistration();
                    signIn(await authApi.me());
                    navigate("/projects");
                  })
                }
              >
                I’ve confirmed my email <Icon name="arrow" size={16} />
              </Button>
              <Button
                variant="subtle"
                loading={busy}
                onClick={() =>
                  void perform(async () => {
                    await authApi.resendConfirmation(email);
                    setMessage("A new confirmation email has been requested.");
                  })
                }
              >
                Resend confirmation email
              </Button>
            </div>
          ) : (
            <form className="auth-form" onSubmit={submit}>
              {mode === "register" && (
                <TextInput
                  label="Full name"
                  placeholder="Your name"
                  value={name}
                  onChange={(e) => setName(e.currentTarget.value)}
                  required
                  maxLength={100}
                  autoComplete="name"
                />
              )}
              {["login", "register", "forgot"].includes(mode) && (
                <TextInput
                  label="Email address"
                  placeholder="you@example.com"
                  value={email}
                  onChange={(e) => setEmail(e.currentTarget.value)}
                  type="email"
                  required
                  autoComplete="email"
                />
              )}
              {["login", "register", "reset"].includes(mode) && (
                <PasswordInput
                  label="Password"
                  placeholder={
                    mode === "login"
                      ? "Enter your password"
                      : "Create a strong password"
                  }
                  value={password}
                  onChange={(e) => setPassword(e.currentTarget.value)}
                  required
                  autoComplete={
                    mode === "login" ? "current-password" : "new-password"
                  }
                  description={
                    mode === "login"
                      ? undefined
                      : "6+ characters with uppercase, lowercase, a number, and a symbol."
                  }
                />
              )}
              {["register", "reset"].includes(mode) && (
                <PasswordInput
                  label="Confirm password"
                  placeholder="Enter your password again"
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.currentTarget.value)}
                  required
                  autoComplete="new-password"
                />
              )}
              {mode === "login" && (
                <div className="auth-options">
                  <Checkbox
                    label="Keep me signed in"
                    checked={remember}
                    onChange={(e) => setRemember(e.currentTarget.checked)}
                    size="sm"
                  />
                  <Link to="/forgot-password">Forgot password?</Link>
                </div>
              )}
              <Button
                type="submit"
                loading={busy}
                fullWidth
                size="md"
                rightSection={<Icon name="arrow" size={17} />}
              >
                {
                  {
                    login: "Sign in",
                    register: "Create account",
                    forgot: "Send reset link",
                    reset: "Update password",
                    confirm: "Confirm email",
                  }[mode]
                }
              </Button>
            </form>
          )}
          {mode === "login" && needsConfirmation && (
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
          {["login", "register"].includes(mode) && !registered && (
            <>
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
                Sample data. No account needed. Your changes stay in this
                browser.
              </p>
            </>
          )}
          {!["login", "register"].includes(mode) && (
            <Link className="auth-back" to="/login">
              <Icon name="back" size={15} /> Back to sign in
            </Link>
          )}
        </div>
        <footer className="auth-footer">
          <Icon name="lock" size={13} /> Your projects. Your perspective.
        </footer>
      </main>
    </div>
  );
}
