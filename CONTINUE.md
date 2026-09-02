# CONTINUE — pick up here (fresh Claude session / new machine)

Read this first, then `docs/DESIGN-v3.md`, then `DECISIONS.md` (esp. D15).

## State (2026-09-02) — v3 Phase A + B DONE, tested in-game

The **Filiaalmanager** (Store Manager) role is fully working in-game. Remaining: **Team Leader**
+ departments (the second role, still deferred).

### Phase B — done & user-verified in-game
- **Custom skill** `sm:skill_storemanager` (primary, D15). Wage lands ~$28–37 (base 46).
- **Recruit via the vanilla Recruitment Agency** — `Interop/Harmony/RecruitmentPatch.cs` postfixes
  `UI.Dialog.RecruitmentSettings.SelectBusiness` and appends the skill to `businessSkills` when the
  player's HQ is selected (no shared `employeePrimarySkills` / agency-settings mutation → zero AI
  contamination). Then schedule at an HQ desk (`Interop/HqDeskAccess.cs` appends the skill to the
  3 desk items' `suitableSkills` via `ItemsGetter.OnItemsLoaded` postfix + city-load backstop).
- **HQ BizMan tab "Filiaalmanagers"** — `Interop/Harmony/BizManTabPatch.cs` (postfix on
  `BizManBusiness.SetUpTabs`: clone the `PurchasingAgents` menu button, insert id into `_tabs`,
  container = a clone of the vanilla `PurchasingAgents` container stripped of scripts/children,
  hosting `UI/StoreManagerTabView`). Native uGUI via `UI/UiKit.cs` (game `Colors` palette, ~2×
  scale, section-rule headers, `[− field +]` steppers). Content: recruit hint, manager adopt,
  per-store Assign/Supervising, and 3 aligned steppers — Weekly budget / Keep stock for N days /
  Order extra %.
- **HQ landing-card counter** — `Interop/Harmony/HqCardPatch.cs` (postfix on
  `HeadquartersList.SetUpEntry`). Gotcha solved: the counter row uses `EqualWidthLabelGroup` with a
  *serialized* label list — the clone's Count + name labels must be `.Add`ed to those lists +
  `ScheduleMatch()` re-invoked, or the row renders left-shifted.
- **Safety margin %** — `StoreAssignment.SafetyMarginPercent` (+ `GlobalDefaults`), applied in
  `DeliveryContracts.ComputeTarget` before the budget cap.
- **Mods panel** — slimmed to: "Filiaalmanager — standaard voor nieuwe winkels" header, 3 default
  sliders, "Veilig verwijderen" dropdown. Full manager/store controls only appear as a fallback
  when `BizManTabPatch.Patched == false`.
- `RoleSystemState.Summary()` reports: desks / HQ tab / agency / hq-card status.

### Phase A (earlier the same day)

- **Skill probe** (`probe/StoreManagerProbe/SKILL-PROBE.md`, run 2026-09-02): runtime `SkillData` +
  `BuildTagCache()` are safe; a mod-skill **primary** bricks a folder-deleted save (load-time
  compat-fix NPE); vanilla managers `baseHourlyWage=30` with a ~0.5 mult. User accepts the
  save-risk → **Option 1** (skills[0] = sm:skill_storemanager, title shows "Filiaalmanager"),
  cheap uninstall safety only, `baseHourlyWage=46`.
- **Phase A coded** (compiles 0/0): `Interop/Harmony/HarmonyBootstrap.cs`,
  `Interop/Harmony/SkillHelperPatches.cs` (prefix on `OnSkillDataLoaded` + postfix on both
  `GetData`), `Interop/SkillRegistry.cs` (runtime SkillData, inject into `SkillHelper.Skills`),
  `Interop/RoleEmployees.cs` (recruit via `GenerateCandidate`+`HireCandidate`, re-skill to
  vanilla), `Runtime/RoleSystemState.cs` (kill-switch: Active/Disabled, structural self-check).
  `GameApi.ManagerSkill` flipped to `sm:skill_storemanager`; `RequireHqShift=false` (v1 — desks
  don't accept the skill yet, so the plan is active when the manager is just assigned to HQ).
  Bundled `mod/StoreManager/Dependencies/` (Harmony 2.10.2 + MonoMod/Cecil, from the user's
  LowerInstallationFee mod). Locales: `sm:skill_storemanager` → Store Manager/Filiaalmanager.
  `ContractSnapshot`/`OriginalContract`/`PendingRestore` **kept** (working+tested — deferred cut).
- **Next up**: **Team Leader** + departments (second native role — `sm:skill_teamleader`, a
  5-field `Department` record, flat pro-rata trim, as a drill-down section inside the same HQ tab).
  See `docs/DESIGN-v3.md` "Deferred".

---

## (historical) Read this first, then `docs/DESIGN-v2.md`, then `DECISIONS.md`.

## What this is

A mod for the game **Big Ambitions** (Hovgaard Games, Steam appid 1331550) that adds a
**Store Manager** you hire to run your shops' day-to-day: keeps them stocked via delivery
contracts, within budgets you set, assigned to N stores. Community request (from a Big Ambitions Discord
discussion). Not related to any other repo.

## State (2026-09-01)

| Thing | Status |
|-------|--------|
| **v1** — off-screen data role (git history ≤ commit `e3c47b8`) | superseded by v2 |
| **v2 design** — `docs/DESIGN-v2.md`, decisions D9–D13 | ✅ locked |
| **v2 Phase 1** — real hired manager (`ba:skill_purchasingagent`), mod supervision plan in `GameInstance.modData`, assign N stores + per-store limits (console + ModOptions panel), weekly (Saturday) delivery-contract restock within budget, toasts + phone digest, reconcile-on-fire | ✅ **coded, compiles clean (0/0) against the real game (Build 3672)** — NOT yet run in-game |
| **v2 Phase 2** — per-store limits in the Options→Mods panel (manager dropdown, store toggles, budget/days/staffing sliders), `StoreManager.SelfTest`, multi-store | ✅ coded, compiles 0/0 |
| **v2 static review** — multi-agent review vs Build 3672 decompile; 19 confirmed bugs (6 save-damage) all fixed — `docs/REVIEW-v2-fixes.md` | ✅ applied, compiles 0/0 |
| **the one in-game test** | ⛔ next — needs a save with an HQ. `StoreManager.SelfTest` (or the probe) runs it. |

The game **auto-updated Build 3670 → 3672** mid-build. The `dotnet build build/CompileCheck.csproj`
check compiles against the live DLLs, so it's the source of truth; the decompile in
it targets Build 3672 —
re-verify any exact signature the compiler flags.

### v2 Phase 1 file map (all under `mod/StoreManager/Scripts/`)
```
Core/StoreManagerMod.cs      Init entry (options panel) + City entry (directory, event wiring, StoreManagerCityMod.Active)
Domain/StoreManagerPlan.cs   StoreManagerPlan, StoreAssignment, ContractSnapshot, GlobalDefaults, WeekTally, StaffingLevel
Interop/GameApi.cs           the ONE game seam — HQ/store/manager reads, modData r/w, ChangeMoney, event sub/unsub
Interop/DeliveryContracts.cs get/snapshot/restore/disable a store's DeliveryContract; PlanAndApply (the weekly tune)
Interop/Feedback.cs          Notifications.Show toasts + Contact/TextMessage phone thread
Interop/Serialization.cs     OdinSerializer JSON envelope of List<StoreManagerPlan>
Interop/ModDataStore.cs      persistentDataPath fallback sink (save-scoped key)
Runtime/ManagerDirectory.cs  owns the plans: Adopt/Drop/Assign/Unassign/SetCap, Reconcile, weekly tick
Runtime/WeeklyRestockPlanner.cs   per-plan weekly pass over assignments
Runtime/WeeklyDigest.cs      compose + send the weekly report
UI/StoreManagerOptions.cs    Options→Mods panel (built-in controls only; store assignment is console in v1)
Debug/StoreManagerCommands.cs  StoreManager.Managers/.Adopt/.Stores/.Assign/.Unassign/.SetCap/.Days/.Status/.PlanWeek
```
v1's `MistakeModel`/`ManagementSkill`/`DifficultyProfile`/`ManagerRank`/`PlayerScheduling`/`sim/`
were deleted (recoverable from git ≤ `e3c47b8`); Phase 3 re-introduces "manager imperfection".

## Artifacts (claude.ai — accessible from any machine)


They describe v1. Update them once v2 Phase 1 lands (or note they're historical).

## Repo layout

```
mod/StoreManager/            the mod (v1 code) — drops into a ModsLocal/StoreManager/ folder
  Scripts/{Core,Domain,Runtime,UI,Interop,Debug,PlayerScheduling}/
  Locales/{en,nl}.json
  StoreManager.asmdef, ModManifest.asset (+ MANIFEST-SETUP.md)
probe/StoreManagerProbe/     throwaway in-game probes (reflection dump, self-test, write-path)
build/
  CompileCheck.csproj        dotnet build vs the real game DLLs (0 errors = type-safe)
  PackMod.csproj / PackProbe.csproj   net472 build -> bin/Release/net472/*.dll
  deploy-local.sh            build + copy into ModsLocal/  ( --probe also deploys the probe )
sim/BalanceSim/              pure-C# 52-week balance sim (vestigial under v2 — MistakeModel unwired)
docs/
  DESIGN-v2.md               THE plan for the rebuild
  research/                  raw workflow output + the reflection dump (real de-obfuscated API)
DECISIONS.md  HANDOFF.md  PHASE0-FINDINGS.md  README.md
```

## New-machine setup

1. **Clone** this repo.
2. **.NET SDK** (`dotnet --version` — any 8+; the mod targets `net472` via reference assemblies,
   which the SDK provides on Windows).
3. **Big Ambitions installed** via Steam. Default path:
   `C:\Program Files (x86)\Steam\steamapps\common\Big Ambitions`
   Managed DLLs: `Big Ambitions_Data\Managed\`. Override the build path with
   `dotnet build build/PackMod.csproj -p:GameManaged="D:\...\Big Ambitions_Data\Managed"`.
4. **Regenerate the decompile** (the in-game reflection dump is the API source of
   truth, but a browsable decompile helps):
   ```
   dotnet tool install -g ilspycmd
   MG="C:/Program Files (x86)/Steam/steamapps/common/Big Ambitions/Big Ambitions_Data/Managed"
   ilspycmd "$MG/BigAmbitions.dll" -p -o ./_decomp/BigAmbitions
   ilspycmd "$MG/BigAmbitions.ModAPI.dll" -o ./_decomp/ModAPI
   ```
   (`_decomp/` is gitignored — regenerate, don't commit; it's ~licensed game content.)
5. **Unity is NOT needed.** Local mods load from a plain folder with one DLL — the Unity Mod
   Builder is only for the Steam Workshop upload.

## Build / deploy / test loop

```
# build both, deploy to ModsLocal (StoreManager + probe)
MODSLOCAL="$HOME/AppData/LocalLow/Hovgaard Games/Big Ambitions/ModsLocal" bash build/deploy-local.sh --probe

# launch the game (needs Steam running). Use -console to get the in-game debug console (backquote key).
powershell -c "Start-Process 'C:\Program Files (x86)\Steam\steamapps\common\Big Ambitions\Big Ambitions.exe' -ArgumentList '-windowed -console'"

# read the log
tail -f "$HOME/AppData/LocalLow/Hovgaard Games/Big Ambitions/Player.log"   # mod lines: [Mod:StoreManager], [SMDUMP], [StoreManagerProbe]
```

Just a compile check, no game launch:
```
dotnet build build/CompileCheck.csproj    # 0 warnings / 0 errors == type-safe against the real game
```

## Gotchas learned the hard way

- **The game locks the mod DLLs while running.** To redeploy: close the game first
  (`taskkill //F //IM "Big Ambitions.exe"`), then copy, then relaunch.
- **`Player.log` is truncated on every launch** (not appended). Grab what you need before relaunching.
- **Debug console** (`ScheduleHelper`-style `CommandHelper.AddCommand`, class in
  `ExternalPlugins.dll` namespace `IngameDebugConsole`): only toggleable if the game was launched
  with `-console` (or is a debug build). Toggle key = **backquote `` ` ``**. Without `-console`,
  use the ModOptions panel instead.
- **The probe's `[ModEntryMainMenu]` auto-loader was removed** — it hijacked the player's save
  choice. It's now opt-in via a `probe-autoload` marker file next to the probe DLL. Leave it off.
- **Save safety**: every probe run so far left the user's saves **byte-identical** (verified with
  `diff -rq` against a backup). `SaveGameManager.LoadAsync` doesn't write. The `SelfTest` command
  injects an in-memory test employee — **don't save the game after running SelfTest**. Always
  back up `SaveGames/` before a session: `cp -r "$HOME/AppData/LocalLow/Hovgaard Games/Big Ambitions/SaveGames" /somewhere/backup`.
- **Test on a save with an HQ + established retail stores + hired staff.** Early-game saves
  (day ~8) have `staffedBusinesses=0` — nothing to manage.
- **`ba:skill_purchasingagent`** is the manager skill (D10). Recruit via the vanilla Recruitment
  Agency; it has no HQ tab so no dual-binding.
- **Delivery is weekly Monday** (D12) — the restock planner runs once/week, not per `onNewDay`.
- **Persistence** goes in `GameInstance.modData["StoreManager.plans.v1"]` (D13) — proven working,
  CosaNostra uses `modData` too.

## The immediate next task

**Test v2 Phase 1 in-game**, then start Phase 2.

### In-game test (needs a save with an HQ + retail stores + a delivery contract)
1. `bash build/deploy-local.sh` ; launch with `-console -windowed` ; load the save.
2. Recruit a **Purchasing Agent** from the Recruitment Agency → hire → assign to the HQ →
   schedule them on an HQ desk (BizMan → HQ → Schedule).
3. Console: `StoreManager.Managers` → `StoreManager.Adopt 0` → `StoreManager.Stores` →
   `StoreManager.Assign 0` → `StoreManager.SetCap 0 8000` → `StoreManager.Status` →
   `StoreManager.PlanWeek`.
4. Confirm from `Player.log` + in-game: the store's `DeliveryContract` got `enabled=true` +
   item amounts tuned within cap; a toast fired; the "Store Manager" phone contact got a
   weekly report; `modData` survives save/reload; firing the manager restores the contract.
5. Fill in `docs/DESIGN-v2.md` §"Still to verify in-game" items 1–4 with what you observe.

### Phase 2 (after the test passes)
- Multi-store: assign 2+ stores to one manager, confirm per-store budget accounting is independent.
- Per-store limits UI: a `ModOption.SpawnUi` store-picker panel (currently console-only).
- Optional: the BizMan HQ "Store Managers" tab — **research spike only, not committed** (both
  critiques rate it highest-risk/lowest-value; needs Harmony on private `BizManBusiness` members).

Keep `dotnet build build/CompileCheck.csproj` green after every file.
