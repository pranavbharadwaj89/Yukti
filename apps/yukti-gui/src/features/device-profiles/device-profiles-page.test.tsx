import { wcagViolations } from "@/a11y/axe";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { DeviceProfilesPage } from "./device-profiles-page";
import { deviceProfilesApi } from "@/services/api-client";

vi.mock("@/services/api-client", () => ({
  deviceProfilesApi: { list: vi.fn(), get: vi.fn(), create: vi.fn(), update: vi.fn(), delete: vi.fn(), testConnection: vi.fn() },
  ApiError: class ApiError extends Error {},
}));

function renderPage() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <DeviceProfilesPage />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("DeviceProfilesPage", () => {
  it("has no WCAG 2.1 A/AA axe violations", async () => {
    vi.mocked(deviceProfilesApi.list).mockResolvedValue([
      { id: "dp-1", name: "Pixel emulator", platform: 0, deviceName: "Pixel 7", endpointKind: 0, version: 1 },
    ]);
    const { container } = renderPage();
    await screen.findByText("Pixel emulator");
    expect(await wcagViolations(container)).toEqual([]);
  });

  it("does not carry a dirty, unsaved name across successive opens of the New profile dialog", async () => {
    // Regression: the create dialog is permanently mounted with a `profile` prop that is always
    // null, so re-seeding keyed only on profile identity never re-fires on a second open --
    // whatever was typed and abandoned on the first open used to leak into the second.
    vi.mocked(deviceProfilesApi.list).mockResolvedValue([]);
    renderPage();
    await screen.findByText("No device profiles yet");

    await userEvent.click(screen.getByRole("button", { name: "New profile" }));
    let dialog = await screen.findByRole("dialog");
    await userEvent.type(within(dialog).getByPlaceholderText("Profile name"), "Abandoned draft");
    await userEvent.click(within(dialog).getByRole("button", { name: "Cancel" }));

    await userEvent.click(screen.getByRole("button", { name: "New profile" }));
    dialog = await screen.findByRole("dialog");
    expect(within(dialog).getByPlaceholderText("Profile name")).toHaveValue("");
  });

  it("preserves an existing profile's extraCapabilities on update instead of wiping them", async () => {
    // Regression: the save payload hardcoded extraCapabilities: null on every save, silently
    // stripping anything set on the profile outside this dialog (e.g. via the API directly).
    vi.mocked(deviceProfilesApi.list).mockResolvedValue([
      { id: "dp-1", name: "Pixel emulator", platform: 0, deviceName: "Pixel 7", endpointKind: 0, version: 1 },
    ]);
    vi.mocked(deviceProfilesApi.get).mockResolvedValue({
      id: "dp-1",
      name: "Pixel emulator",
      platform: 0,
      platformVersion: null,
      deviceName: "Pixel 7",
      app: null,
      appiumUrl: "http://localhost:4723",
      endpointKind: 0,
      farmCredentialRef: null,
      reset: 1,
      sessionTimeoutSeconds: 300,
      extraCapabilities: { "appium:noReset": true },
      version: 1,
    });
    vi.mocked(deviceProfilesApi.update).mockResolvedValue(undefined);
    renderPage();
    await screen.findByText("Pixel emulator");

    await userEvent.click(screen.getByRole("button", { name: "Edit" }));
    const dialog = await screen.findByRole("dialog");
    await userEvent.click(within(dialog).getByRole("button", { name: "Save changes" }));

    expect(deviceProfilesApi.update).toHaveBeenCalledWith(
      "dp-1",
      expect.objectContaining({ extraCapabilities: { "appium:noReset": true } }),
    );
  });

  it("creates a profile with extraCapabilities left null — there is no field for it yet", async () => {
    vi.mocked(deviceProfilesApi.list).mockResolvedValue([]);
    vi.mocked(deviceProfilesApi.create).mockResolvedValue({ profileId: "dp-new" });
    renderPage();
    await screen.findByText("No device profiles yet");

    await userEvent.click(screen.getByRole("button", { name: "New profile" }));
    const dialog = await screen.findByRole("dialog");
    await userEvent.type(within(dialog).getByPlaceholderText("Profile name"), "New emulator");
    await userEvent.click(within(dialog).getByRole("button", { name: "Create" }));

    expect(deviceProfilesApi.create).toHaveBeenCalledWith(
      expect.objectContaining({ name: "New emulator", extraCapabilities: null }),
    );
  });
});
