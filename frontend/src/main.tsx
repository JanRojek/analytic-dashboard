import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { MantineProvider, createTheme } from "@mantine/core";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { SessionProvider } from "./components/SessionProvider";
import "@mantine/core/styles.css";
import "./index.css";
import App from "./App";

const theme = createTheme({
  primaryColor: "forest",
  black: "#26362e",
  primaryShade: 7,
  colors: {
    forest: [
      "#f0f6f1",
      "#e2ede4",
      "#c4dbca",
      "#a2c4ac",
      "#7eab8b",
      "#5e9270",
      "#417c59",
      "#2c654b",
      "#24533e",
      "#1e4435",
    ],
  },
  fontFamily:
    '"Segoe UI", -apple-system, BlinkMacSystemFont, Arial, sans-serif',
  headings: { fontFamily: "inherit", fontWeight: "600" },
  defaultRadius: 6,
  cursorType: "pointer",
  fontSizes: { xs: "11px", sm: "13px", md: "14px", lg: "16px", xl: "20px" },
  components: {
    Button: {
      defaultProps: { size: "sm" },
      styles: { root: { fontWeight: 550, height: 38 }, inner: { gap: 7 } },
    },
    TextInput: {
      styles: {
        label: { fontWeight: 550, marginBottom: 6, fontSize: 13 },
        input: { height: 40, fontSize: 13 },
      },
    },
    PasswordInput: {
      styles: {
        label: { fontWeight: 550, marginBottom: 6, fontSize: 13 },
        input: { minHeight: 40 },
        innerInput: { fontSize: 13 },
      },
    },
    Select: {
      styles: {
        label: { fontWeight: 550, marginBottom: 6, fontSize: 13 },
        input: { height: 38, fontSize: 13 },
      },
    },
    Textarea: {
      styles: {
        label: { fontWeight: 550, marginBottom: 6, fontSize: 13 },
        input: { fontSize: 13 },
      },
    },
    Modal: {
      defaultProps: {
        overlayProps: { backgroundOpacity: 0.25, blur: 2 },
        padding: 28,
        radius: 12,
      },
      styles: {
        title: { fontWeight: 600, fontSize: 20, letterSpacing: "-.5px" },
        header: { paddingBottom: 20 },
      },
    },
    Menu: { defaultProps: { radius: 8 } },
  },
});
const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 20_000, retry: 1, refetchOnWindowFocus: false },
    mutations: { retry: false },
  },
});

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <MantineProvider theme={theme} forceColorScheme="light">
      <QueryClientProvider client={queryClient}>
        <BrowserRouter>
          <SessionProvider>
            <App />
          </SessionProvider>
        </BrowserRouter>
      </QueryClientProvider>
    </MantineProvider>
  </StrictMode>,
);
