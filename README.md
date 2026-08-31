# Store Manager Mod — Big Ambitions

Hire a **Manager** or **Assistant Manager** for a store; they run its day-to-day operations
(schedule, stock, sick/holiday, complaints, training) so the business keeps running while
you focus elsewhere. Plus: schedule *yourself* into a store roster, and get released from a
station automatically when a scheduled employee clocks in.

Full design: see the design brief and the Phase 0 runbook (links in `DECISIONS.md`).

## Status

| Part | State |
|------|-------|
| Design brief | ✅ complete (artifact) |
| Toolchain decision | ✅ locked — official SDK (`BAModAPI`), see `DECISIONS.md` |
| Domain model (skill, mistakes, policy, difficulty, wages) | ✅ implemented — pure C#, no game deps |
| SDK entry points / manifest / asmdef | ✅ scaffolded against the real SDK API |
| **Phase 0** | ✅ **done** — decompiled the game, mapped every type, mod compiles + **runs in the real game** (Build 3670), all SDK hooks fire, all read bindings verified live (`REPORT.md`). Verdict: **GO** |
| Game bindings (`Interop/GameBindings.cs`) | ✅ real implementations; ~10 `// VERIFY` notes (field names / wholesale path / one behavioural check) |
| Build & deploy | ✅ `dotnet` (net472) → `build/deploy-local.sh`. **No Unity needed** — local mods just need one DLL in `ModsLocal/<name>/` |
| Write-path check (task/shift honoured by sim) | ⏳ needs a save with hired employees (test save had none) |
| Phase 1: hiring UI, playtest, balance | ⛔ next |

## Layout

```
mod/StoreManager/          drop into  <SDK clone>/Assets/Mods/StoreManager/
  StoreManager.asmdef      links all canonical game DLLs (copied from Example-Options)
  ModManifest.asset        Big Ambitions/Mod Manifest ScriptableObject
  Locales/                 en.json, nl.json
  Scripts/
    Core/                  IModBigAmbitions entry points (init + city load)
    Domain/                pure C# — ManagerRank, ManagementSkill, StorePolicy,
                           DifficultyProfile, MistakeModel, StoreManagerData
    Runtime/               ManagedStore (daily loop), DailyOperations, ManagerDirectory,
                           WeeklyDigest
    UI/                    PolicyOptions — OptionsService / ModOptions panel
    PlayerScheduling/      PlayerShift, RegisterHandoff
    Interop/               GameBindings — the ONLY file that touches game types.
                           Everything Phase 0 must confirm is here, marked `// PHASE0:`.
probe/StoreManagerProbe/   throwaway mod: proves the 3 writes (reassign task /
                           write shift / trigger restock). Not shipped.
```

## Build (once you have the SDK + game)

1. Clone the SDK: `git clone https://github.com/hovgaardgames/bigambitions`
2. Open in Unity **2022.3.62f2**, accept the DLL-import prompt (`BA_GAME_DLLS_IMPORTED`).
3. Copy `mod/StoreManager/` → `<sdk>/Assets/Mods/StoreManager/`.
4. Resolve every `// PHASE0:` marker in `Scripts/Interop/GameBindings.cs` against the
   decompiled game assemblies (see the Phase 0 runbook).
5. **Big Ambitions → Mod Builder → Build & Install.**
6. Test from `ModsLocal`, then upload via the in-game Mod Creator.

Run the probe mod (step 3–5 with `probe/StoreManagerProbe/`) *first* — it is the Phase 0
go/no-go gate.
