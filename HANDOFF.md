# Handoff — what's done, what's left, in what order

## Done (no game needed — done in this environment)

- **All design decisions locked** — `DECISIONS.md` (D1–D8), mirrored into the design-brief artifact.
- **Toolchain resolved** from reading the real SDK source: official SDK (`BAModAPI`), not BepInEx.
- **Domain layer — fully implemented, pure C#, unit-testable today:**
  `ManagerRank`, `ManagementSkill` (1–5 → mistake chance / severity / span), `StorePolicy`,
  `DifficultyProfile` (easy/normal/hard), `MistakeModel` (deterministic daily roll),
  `StoreManagerData` + `WeekTally`.
- **Runtime layer — implemented against a clean interface:** `ManagedStore` (daily loop),
  `DailyOperations` (restock/schedule/leave/complaints/training steps), `ManagerDirectory`
  (hiring, wages, quit checks, day/week fan-out), `WeeklyDigest`.
- **UI:** `PolicyOptions` — the policy panel via `OptionsService`/`ModOptions` (confirmed SDK API).
- **Player scheduling:** `PlayerShift` / `PlayerScheduleBook` / `RegisterHandoff` (Stuart's bug).
- **The game seam:** `Scripts/Interop/GameBindings.cs` — `IGameBindings` lists every single
  game touchpoint the mod needs; `GameBindingsLive` implements each as a throwing stub with a
  `// PHASE0:` note pointing at the likely DLL.
- **Project shell:** SDK-correct `StoreManager.asmdef` (byte-for-byte the SDK reference set),
  `ModManifest.asset` (+ `MANIFEST-SETUP.md`), `Locales/en.json` + `nl.json`.
- **Phase 0 probe:** `probe/StoreManagerProbe/` — runnable SDK mod, F8/F9/F10/F11 hotkeys,
  `REPORT.md` template.

## Left — needs the game + Unity 2022.3.62f2 (can't be done here)

Do these in order. Each step unblocks the next.

### 1. Stand up the SDK  *(~half day)*
- Install Big Ambitions (Steam) + Unity Hub + Unity **2022.3.62f2** + macOS build support.
- `git clone https://github.com/hovgaardgames/bigambitions`, open in Unity, accept the
  DLL-import prompt (defines `BA_GAME_DLLS_IMPORTED`).
- Build & install one sample mod (e.g. Example-Options) to confirm the toolchain.
- Copy `mod/StoreManager/` and `probe/StoreManagerProbe/` into `<sdk>/Assets/Mods/`.
- Fix `ModManifest.asset` per `MANIFEST-SETUP.md` (recreate via the menu, relink fields).
- Record: is the shipped game **Mono or IL2CPP**?

### 2. Phase 0 — resolve the seam  *(~2 days)*  → see the Phase 0 runbook artifact
- Decompile the imported DLLs (ILSpy / dnSpy). Fill the code-map table in `REPORT.md`.
- Fill the `// TODO`s in `probe/StoreManagerProbe/Scripts/ProbeMod.cs` with real type names.
- Build & install the probe, load a city with a staffed store, press F8→F11, record results.
- Resolve **every** `// PHASE0:` marker in `GameBindings.cs` — replace each `throw Todo(...)`
  with the real one-line call. When done, `GameBindingsLive` is the real implementation.
- **Go / conditional (D2 — bundle `0Harmony.dll`) / no-go (numbers-only re-scope).**

### 3. Phase 1 — core, one store  *(build against the now-real GameBindings)*
- Wire a minimal hiring entry point (extend `ManagerDirectory.AssignManager`) — a debug
  command is fine for the first playtest; a proper UI (a store-panel button, or a
  `RadzenButton`-equivalent native window) comes after it works.
- Playtest the daily loop + weekly digest against a real store. Tune the numbers in
  `ManagementSkill` / `MistakeModel` / `DifficultyProfile` (all placeholders today).
- Confirm save round-trip: assign a manager, save, reload, uninstall the mod, reload.

### 4. Phase 2 — player scheduling + register handoff
- Add the player to the schedule UI as a schedulable entity (`PlayerScheduleBook`).
- Hook `RegisterHandoff.OnEmployeeClockIn` to the employee shift-start event found in step 2.

### 5. Phase 3 — optional, only if v1 lands
- Team Leaders + departments (departments are mod-invented — see D7), multi-store managers.

## If you only have 30 minutes

Read `DECISIONS.md`, then `Scripts/Interop/GameBindings.cs` top to bottom — that file *is*
the integration plan.
