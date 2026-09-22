import test from "node:test";
import assert from "node:assert/strict";
import {
  apiAdapter,
  authApi,
  ApiError,
  subscribeToSessionExpiry,
} from "../src/data/api.ts";

const user = {
  id: "user-id",
  displayName: "Test Analyst",
  email: "analyst@example.com",
  createdAtUtc: "2026-09-20T00:00:00Z",
};
const jsonResponse = (body, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });

async function withFetch(mock, action) {
  const original = globalThis.fetch;
  globalThis.fetch = mock;
  try {
    await action();
  } finally {
    globalThis.fetch = original;
  }
}

test("auth operations use the exact backend contracts and include cookies", async () => {
  const requests = [];
  await withFetch(
    async (url, options) => {
      requests.push({ url, options });
      if (url.endsWith("/register")) return jsonResponse(user, 201);
      if (url.endsWith("/me")) return jsonResponse(user);
      if (url.endsWith("/registration-status"))
        return jsonResponse({ status: "Confirmed" });
      return new Response(null, { status: 204 });
    },
    async () => {
      await authApi.login(user.email, "Example1!", false);
      assert.deepEqual(
        await authApi.register(user.displayName, user.email, "Example1!"),
        user,
      );
      await authApi.confirmEmail(user.id, "a+b/=");
      assert.deepEqual(await authApi.registrationStatus(), {
        status: "Confirmed",
      });
      await authApi.completeRegistration();
      await authApi.resendConfirmation(user.email);
      await authApi.forgotPassword(user.email);
      await authApi.resetPassword(user.id, "a+b/=", "Example2!");
      assert.deepEqual(await authApi.me(), user);
      await authApi.logout();
    },
  );
  assert.deepEqual(
    requests.map(({ url }) => url),
    [
      "/api/auth/login",
      "/api/auth/register",
      "/api/auth/confirm-email",
      "/api/auth/registration-status",
      "/api/auth/complete-registration",
      "/api/auth/resend-confirmation",
      "/api/auth/forgot-password",
      "/api/auth/reset-password",
      "/api/auth/me",
      "/api/auth/logout",
    ],
  );
  assert.ok(requests.every(({ options }) => options.credentials === "include"));
  assert.ok(
    requests.every(({ options }) => options.signal instanceof AbortSignal),
  );
  assert.deepEqual(
    requests
      .filter(({ options }) => options.body)
      .map(({ options }) => JSON.parse(options.body)),
    [
      { email: user.email, password: "Example1!", rememberMe: false },
      {
        displayName: user.displayName,
        email: user.email,
        password: "Example1!",
      },
      { userId: user.id, token: "a+b/=" },
      { email: user.email },
      { email: user.email },
      { userId: user.id, token: "a+b/=", newPassword: "Example2!" },
    ],
  );
  assert.ok(
    requests
      .filter(
        ({ url }) =>
          !url.endsWith("/me") && !url.endsWith("/registration-status"),
      )
      .every(({ options }) => options.method === "POST"),
  );
});

test("only protected-request 401 responses report expired sessions", async () => {
  let expired = 0;
  const unsubscribe = subscribeToSessionExpiry(() => {
    expired++;
  });
  const incorrect = { detail: "The email or password is incorrect." };
  try {
    await withFetch(
      async () => jsonResponse(incorrect, 401),
      async () => {
        for (const action of [
          () => authApi.login(user.email, "incorrect", false),
          () => authApi.register(user.displayName, user.email, "Example1!"),
          () => authApi.registrationStatus(),
          () => authApi.completeRegistration(),
          () => authApi.confirmEmail(user.id, "expired-token"),
        ])
          await assert.rejects(
            action(),
            (error) =>
              error instanceof ApiError &&
              error.status === 401 &&
              error.message === incorrect.detail,
          );
        assert.equal(expired, 0);
        for (const action of [
          () => apiAdapter.listProjects(),
          () => apiAdapter.getDataset("project-id", "dataset-id"),
          () => authApi.me(),
          () => authApi.logout(),
        ])
          await assert.rejects(
            action(),
            (error) => error instanceof ApiError && error.status === 401,
          );
        assert.equal(expired, 4);
        unsubscribe();
        await assert.rejects(authApi.me(), ApiError);
        assert.equal(expired, 4);
      },
    );
  } finally {
    unsubscribe();
  }
});

test("a broken expiry subscriber does not hide the original API failure", async () => {
  let notified = false;
  const unsubscribeBroken = subscribeToSessionExpiry(() => {
    throw new Error("Subscriber failure");
  });
  const unsubscribeHealthy = subscribeToSessionExpiry(() => {
    notified = true;
  });
  try {
    await withFetch(
      async () => new Response(null, { status: 401 }),
      async () => {
        await assert.rejects(
          authApi.me(),
          (error) => error instanceof ApiError && error.status === 401,
        );
        assert.equal(notified, true);
      },
    );
  } finally {
    unsubscribeBroken();
    unsubscribeHealthy();
  }
});

test("network failures, timeouts and server errors reject without expiring sessions or substituting demo data", async () => {
  let expired = 0;
  const unsubscribe = subscribeToSessionExpiry(() => {
    expired++;
  });
  try {
    await withFetch(
      async () => {
        throw new TypeError("Failed to fetch");
      },
      async () => {
        await assert.rejects(
          apiAdapter.listProjects(),
          (error) =>
            error instanceof ApiError &&
            error.status === 0 &&
            /Cannot reach the server/.test(error.message),
        );
      },
    );
    await withFetch(
      async () => {
        throw new DOMException("The operation timed out", "TimeoutError");
      },
      async () => {
        await assert.rejects(
          authApi.me(),
          (error) =>
            error instanceof ApiError &&
            error.status === 0 &&
            /too long/.test(error.message),
        );
      },
    );
    await withFetch(
      async () => new Response("<html>Unavailable</html>", { status: 503 }),
      async () => {
        await assert.rejects(
          apiAdapter.listProjects(),
          (error) =>
            error instanceof ApiError &&
            error.status === 503 &&
            /server could not/.test(error.message),
        );
      },
    );
    assert.equal(expired, 0);
  } finally {
    unsubscribe();
  }
});

test("validation problems preserve useful backend messages", async () => {
  await withFetch(
    async () =>
      jsonResponse(
        { errors: { Name: ["Project name cannot be empty."] } },
        400,
      ),
    async () => {
      await assert.rejects(
        apiAdapter.createProject(""),
        (error) =>
          error instanceof ApiError &&
          error.status === 400 &&
          error.message === "Project name cannot be empty.",
      );
    },
  );
});

test("CSV import preserves multipart boundaries and accepted job identifiers", async () => {
  const file = new File(["region,revenue\nEurope,42"], "sales.csv", {
    type: "text/csv",
  });
  const accepted = {
    datasetId: "dataset-id",
    datasetVersionId: "version-id",
    importJobId: "job-id",
  };
  await withFetch(
    async (url, options) => {
      assert.equal(url, "/api/projects/project-id/datasets/import/csv");
      assert.equal(options.method, "POST");
      assert.equal(options.credentials, "include");
      assert.ok(options.body instanceof FormData);
      assert.equal(options.body.get("file").name, file.name);
      assert.equal(options.headers["Content-Type"], undefined);
      return jsonResponse(accepted, 202);
    },
    async () => {
      assert.deepEqual(
        await apiAdapter.importCsv("project-id", file),
        accepted,
      );
    },
  );
});

test("project pagination and query results are mapped from actual API responses", async () => {
  const requested = [];
  await withFetch(
    async (url, options) => {
      requested.push(url);
      if (url.includes("page=1"))
        return jsonResponse({
          items: [{ id: "one", name: "First" }],
          totalPages: 2,
        });
      if (url.includes("page=2"))
        return jsonResponse({
          items: [{ id: "two", name: "Second" }],
          totalPages: 2,
        });
      assert.deepEqual(JSON.parse(options.body), {
        groupByColumn: "region",
        measureColumn: "revenue",
        aggregation: "Sum",
      });
      return jsonResponse({ items: [{ label: "Europe", value: 42 }] });
    },
    async () => {
      assert.deepEqual(
        (await apiAdapter.listProjects()).map((project) => project.id),
        ["one", "two"],
      );
      assert.deepEqual(
        await apiAdapter.query("project-id", "dataset-id", {
          groupByColumn: "region",
          measureColumn: "revenue",
          aggregation: "Sum",
        }),
        [{ label: "Europe", value: 42 }],
      );
    },
  );
  assert.deepEqual(requested, [
    "/api/projects?page=1&pageSize=100",
    "/api/projects?page=2&pageSize=100",
    "/api/projects/project-id/datasets/dataset-id/query",
  ]);
});
