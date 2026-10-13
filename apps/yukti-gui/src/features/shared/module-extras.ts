import type { ComponentType } from "react";
import {
  MobilePreRunPanel,
  buildMobileVariableOverrides,
  emptyMobileDeviceConfig,
  type MobileDeviceConfig,
} from "@/features/testing/mobile-pre-run-panel";

// D4-T2: the registry `no-module-kind-branching` exists for. A module that
// needs something beyond its schema-driven step form — Mobile needs an
// Appium device config supplied as run-time variable overrides, not step
// params — gets ONE entry here, keyed by its module kind. Every component
// that uses this (ModuleTestForm, for now) does a plain object lookup,
// never an `if (moduleKind === ...)`/`switch` branch: this file is the
// only place a module kind is named as a literal for behavioral branching.
//
// The one other per-module special case in the app — API Studio's richer
// request builder for api.request — doesn't need an entry here at all: it
// lives on its own route/page (features/api-studio), reached by
// navigation, not by a branch inside a shared component.
export interface ModuleExtra<TConfig> {
  emptyConfig: TConfig;
  PreRunPanel: ComponentType<{ config: TConfig; onChange: (config: TConfig) => void }>;
  buildVariableOverrides: (config: TConfig) => Record<string, unknown> | undefined;
}

// A dictionary of per-module entries, each internally consistent but with
// a different hidden TConfig — TypeScript has no existential-type syntax
// for that, so the registry's own declaration is the one place `any` is
// the correct, standard way to express it (every individual entry below
// is still fully typed against its own TConfig via `satisfies`).
// eslint-disable-next-line @typescript-eslint/no-explicit-any
export const MODULE_EXTRAS: Partial<Record<string, ModuleExtra<any>>> = {
  mobile: {
    emptyConfig: emptyMobileDeviceConfig,
    PreRunPanel: MobilePreRunPanel,
    buildVariableOverrides: buildMobileVariableOverrides,
  } satisfies ModuleExtra<MobileDeviceConfig>,
};
