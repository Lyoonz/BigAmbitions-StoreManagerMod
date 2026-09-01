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

- **Phase 0 — DONE.** Decompiled the game, mapped every type (`PHASE0-FINDINGS.md`), rewrote
  `GameBindings.cs` against real types, built with `dotnet` (net472), packaged to `ModsLocal/`,
  and **ran it in the real game** (Build 3670). All SDK hooks fired; the probe auto-loaded a
  save and verified every read binding against live data, zero exceptions, zero save writes
  (`probe/REPORT.md`). **Verdict: GO.** No Unity was needed — local mods just need one DLL.

## Left

Phase 0 has one loose end: the write-path check (does the sim keep a mod-written task/shift)
needs a save **with hired employees** — the test save had none. Plus the wholesale purchase
path and a few `// VERIFY` field names. Everything else is proven.

### 1. Build & deploy  *(done — repeatable)*
- `bash build/deploy-local.sh` builds the mod with `dotnet` (net472) and drops it in
  `ModsLocal/StoreManager/`. Add `--probe` to also deploy the headless test probe.
- Launch `steam://rungameid/1331550//-windowed`, then read `Player.log`
  (`~/AppData/LocalLow/Hovgaard Games/Big Ambitions/Player.log`).
- **No Unity / SDK clone needed.** The Unity Mod Builder is only for the Steam Workshop upload;
  `ModManifest.asset` is not read at runtime. (For the Workshop upload later, the SDK route in
  `MANIFEST-SETUP.md` still applies.)

### 2. Phase 0 — one loose end  *(~30 min, needs a staffed save)*
- Read path ✅ and restock write-path ✅ both verified in-game already.
- Only left: on a save that has **hired employees**, run the probe (`--probe`) and confirm
  probe #1 (a mod-set `assignedWorkStationItems` sticks) and #2 (`ScheduleDay.AddWorkShift`
  shows in the BizMan schedule, employee turns up). The test save had 0 staff anywhere.
- Then clear the last few `// VERIFY` field-name notes off the probe dump.

### 3. Phase 1 — core, one store  *(runs end-to-end; needs playtest tuning)*
- **Done & verified in-game:** hiring (options-menu buttons + `StoreManager.Hire/.Status/.RunDay/
  .RunWeek`), and the full runtime loop — `ManagedStore → DailyOperations → MistakeModel →
  WeeklyDigest` — runs 7 days end-to-end against a live store with zero exceptions.
  `StoreManager.SelfTest <skill>` re-runs that whole validation anytime.
- **Left (needs a human playing, time advancing):**
  1. Load a save with a hired shop employee, `StoreManager.Hire manager 4`, play a real week.
  2. Watch the weekly digest message; check `StoreManager.Status`.
  3. Tune `ManagementSkill` / `MistakeModel` / `DifficultyProfile` against how "poor vs great"
     *feels* — `sim/BalanceSim` gives the numbers, playing gives the verdict.
  4. Confirm the save round-trips with a manager assigned (`ModDataStore` file).

### 4. Phase 2 — player scheduling + register handoff
- Add the player to the schedule UI as a schedulable entity (`PlayerScheduleBook`).
- Hook `RegisterHandoff.OnEmployeeClockIn` to the employee shift-start event found in step 2.

### 5. Phase 3 — optional, only if v1 lands
- Team Leaders + departments (departments are mod-invented — see D7), multi-store managers.

## If you only have 30 minutes

Read `DECISIONS.md`, then `Scripts/Interop/GameBindings.cs` top to bottom — that file *is*
the integration plan.
