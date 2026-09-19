import { useState } from "react";
import {
    Button,
    Center,
    Divider,
    Paper,
    PasswordInput,
    SegmentedControl,
    Stack,
    Text,
    TextInput,
    Title,
} from "@mantine/core";
import { useForm } from "@mantine/form";

type AuthMode = "login" | "register";

type AuthFormValues = {
    email: string;
    password: string;
    confirmPassword: string;
};

function GoogleIcon() {
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
                d="M9 18c2.43 0 4.468-.806 5.956-2.18l-2.91-2.258c-.805.54-1.835.859-3.046.859-2.344 0-4.328-1.585-5.037-3.714H.956v2.332A9 9 0 0 0 9 18Z"
            />
            <path
                fill="#FBBC05"
                d="M3.963 10.707A5.41 5.41 0 0 1 3.682 9c0-.593.102-1.17.281-1.707V4.961H.956A9 9 0 0 0 0 9c0 1.452.347 2.827.956 4.039l3.007-2.332Z"
            />
            <path
                fill="#EA4335"
                d="M9 3.58c1.322 0 2.508.455 3.441 1.346l2.582-2.582C13.464.892 11.427 0 9 0A9 9 0 0 0 .956 4.961l3.007 2.332C4.672 5.165 6.656 3.58 9 3.58Z"
            />
        </svg>
    );
}

function LoginPage() {
    const [mode, setMode] = useState<AuthMode>("login");

    const form = useForm<AuthFormValues>({
        mode: "controlled",

        initialValues: {
            email: "",
            password: "",
            confirmPassword: "",
        },

        validate: (values) => ({
            email: values.email.trim().length === 0
                ? "Email is required"
                : null,

            password: values.password.length === 0
                ? "Password is required"
                : null,

            confirmPassword:
                mode === "register" &&
                values.confirmPassword !== values.password
                    ? "Passwords do not match"
                    : null,
        }),
    });

    const handleModeChange = (value: string) => {
        setMode(value as AuthMode);
        form.clearErrors();
    };

    const handleSubmit = (values: AuthFormValues) => {
        console.log(mode, values);
    };

    return (
        <Center mih="100vh" px="md">
            <Stack w="100%" maw={420} gap="lg">
                <Text ta="center" fw={700} size="xl">
                    Analytic Dashboard
                </Text>

                <Paper
                    withBorder
                    radius="lg"
                    p="xl"
                >
                    <Stack gap="md">
                        <SegmentedControl
                            value={mode}
                            onChange={handleModeChange}
                            data={[
                                { label: "Sign in", value: "login" },
                                { label: "Register", value: "register" },
                            ]}
                        />

                        <Stack gap={4} align="center">
                            <Title order={2} ta="center">
                                {mode === "login"
                                    ? "Welcome back"
                                    : "Create your account"}
                            </Title>

                            <Text c="dimmed" size="sm" ta="center">
                                {mode === "login"
                                    ? "Sign in to continue to your projects."
                                    : "Create an account to get started."}
                            </Text>
                        </Stack>

                        <Stack gap="md">
                            <Button
                                variant="default"
                                h={46}
                                radius="md"
                                leftSection={<GoogleIcon />}
                                styles={{
                                    root: {
                                        borderColor: "var(--mantine-color-gray-3)",
                                        boxShadow: "0 1px 2px rgba(0, 0, 0, 0.04)",
                                    },
                                    label: {
                                        fontWeight: 500,
                                    },
                                }}
                            >
                                Continue with Google
                            </Button>

                            <Divider
                                label="or"
                                labelPosition="center"
                                mt="sm"
                            />

                            <form onSubmit={form.onSubmit(handleSubmit)}>
                                <Stack gap="md">
                                    <TextInput
                                        label="Email"
                                        placeholder="Enter your email address"
                                        {...form.getInputProps("email")}
                                    />

                                    <PasswordInput
                                        label="Password"
                                        placeholder="Enter your password"
                                        {...form.getInputProps("password")}
                                    />

                                    {mode === "register" && (
                                        <PasswordInput
                                            label="Confirm password"
                                            placeholder="Repeat your password"
                                            {...form.getInputProps("confirmPassword")}
                                        />
                                    )}

                                    <Button
                                        type="submit"
                                        mt="xs"
                                    >
                                        {mode === "login"
                                            ? "Sign in"
                                            : "Create account"}
                                    </Button>
                                </Stack>
                            </form>
                        </Stack>
                    </Stack>
                </Paper>
            </Stack>
        </Center>
    );
}

export default LoginPage;
