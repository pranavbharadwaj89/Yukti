import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "@tanstack/react-router";
import "./index.css";
import { router } from "@/app/router";
import { restoreSession } from "@/services/api-client";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: 1,
      staleTime: 10_000,
    },
  },
});

// Restores the session from the sessionStorage refresh token, if any,
// BEFORE the router's first render — otherwise the guard on every
// authenticated route would redirect to /login on every reload, even
// with a perfectly valid refresh token sitting in sessionStorage.
void restoreSession().finally(() => {
  createRoot(document.getElementById("root")!).render(
    <StrictMode>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </StrictMode>,
  );
});
