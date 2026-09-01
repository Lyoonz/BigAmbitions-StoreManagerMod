# CONTINUE — pick up here (fresh Claude session / new machine)

Read this first, then `docs/DESIGN-v2.md`, then `DECISIONS.md`.

## What this is

A mod for the game **Big Ambitions** (Hovgaard Games, Steam appid 1331550) that adds a
**Store Manager** you hire to run your shops' day-to-day: keeps them stocked via delivery
contracts, within budgets you set, assigned to N stores. Community request (Discord: lilnyce,
StuartArmour, Lyoon). Not related to any other repo.

## State (2026-09-01)

| Thing | Status |
|-------|--------|
| **v1** — off-screen data role, hire via console/options button, daily loop, restock via DeliveryContract | ✅ **built, runs in the real game, verified end-to-end** (see `probe/StoreManagerProbe/REPORT.md`). The user rejected the UX (invisible, not "via the office"). |
| **v2 design** — manager = real hired employee, mod supervision plan, weekly Monday restock, ModOptions panel, `modData` persistence | ✅ **researched (multi-agent workflow), verified in-game (reflection dump), decisions locked D9–D13** — `docs/DESIGN-v2.md` |
| **v2 build** | ⛔ **not started.** This is the next job. |

The code on disk (`mod/StoreManager/Scripts/`) is still **v1**. `docs/DESIGN-v2.md` §"Phased plan"
lists the file-by-file Phase 1 rewrite.

## Artifacts (claude.ai — accessible from any machine)

- Design brief: https://claude.ai/code/artifact/c3ae27f3-29b9-48a5-94e3-71116b82e955
- Phase 0 runbook: https://claude.ai/code/artifact/f2682273-e63e-457c-be0a-daeb40e50ab4

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
4. **Regenerate the decompile** (the `docs/research/*.txt` reflection dump is the API source of
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

Execute **Phase 1** of `docs/DESIGN-v2.md`. Order:
1. `Interop/GameBindings.cs` seam rewrite (HQ discovery, schedule gate, modData, dual-binding guard)
2. `Domain/StoreManagerPlan.cs` + `StoreAssignment.cs` (replace `StoreManagerData.cs`)
3. `Interop/DeliveryContracts.cs` + `Interop/Feedback.cs`
4. `Runtime/ManagerDirectory.cs` + `ManagedPlan.cs` + `DailyOperations.Restock` (weekly)
5. `UI/StoreManagerPanel.cs` (ModOption.SpawnUi) + console commands
6. `Core/StoreManagerMod.cs` wiring; locales
7. In-game test the full flow; resolve the 4 open verifications in `docs/DESIGN-v2.md`
   §"Still to verify in-game".

Keep `dotnet build build/CompileCheck.csproj` green after every file.
