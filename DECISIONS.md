# Decision log — Store Manager Mod

Decisions made while executing the plan of approach, with the evidence they rest on.
Mirrored into the design-brief artifact.

- Design brief: https://claude.ai/code/artifact/c3ae27f3-29b9-48a5-94e3-71116b82e955
- Phase 0 runbook: https://claude.ai/code/artifact/f2682273-e63e-457c-be0a-daeb40e50ab4

## D1 — Toolchain: the official SDK, not BepInEx

**Decision.** Build on the official modding SDK (`BAModAPI`, shipped in `BigAmbitions.ModAPI.dll`).

**Evidence.** The SDK's example-mod asmdef (`Example-Options.asmdef`) sets `"overrideReferences": true`
and lists **every** game DLL as a precompiled reference — including `BigAmbitions.dll`,
`BigAmbitions.Characters.dll`, `BigAmbitions.AI.dll`, `BigAmbitions.Neighborhoods.dll`,
`BigAmbitions.Legacy.dll`, `OdinSerializer.dll`, `Google.OrTools.dll`. The example mods call
game internals directly: `GameManager.ChangeMoneySafe(...)`, `SaveGameManager.Current`,
`saveGame.VehicleInstances`, `BuildingManager.Instance`, `BuildingHelper.GetBuilding(Address)`,
`ItemsGetter.AllItems`, and freely mutate live game-data objects (patch shelves, add to importer
settings) with a save/restore-on-unload pattern.

So a mod already has **full public reach** into the workforce/economy systems. BepInEx's value —
runtime access to game code — is already provided. BepInEx would cost us: the first-party loader,
`OptionsService` UI, smartphone messaging, `ModdingAPI` registration hooks, and in-game Mod Creator
distribution.

**Consequence.** `Interop/GameBindings.cs` is the single seam that touches game types. If Phase 0
finds a hook that direct access can't reach (subscribe to a per-tick/per-day event, intercept a
method mid-call), see D2 — not a switch to BepInEx.

## D2 — Patching, if needed: Harmony as a bundled dependency

**Decision.** If a runtime hook is unreachable by direct calls, ship `0Harmony.dll` as a flat
managed DLL in the mod's `Dependencies/` folder and patch from inside the SDK mod.

**Evidence.** `ModValidator` rule 12: "The Dependencies folder must be flat with only `.dll` files"
— third-party managed DLLs are an expected, validated case. Nothing in the validator restricts
gameplay manipulation or method patching.

**Consequence.** Still one mod, one loader, one distribution channel. Harmony is opt-in and isolated
to whichever `GameBindings` method needs it.

## D3 — Manager is an off-screen role, not a spawned NPC (v1)

**Decision.** A hired manager is a data role attached to a business/building. No character model,
pathfinding, or floor presence in v1.

**Evidence / rationale.** Matches the game's existing meta-roles (HR Manager, Logistics Manager,
Pricing Manager). Physical presence is high cost (Behavior Designer trees — `BehaviorDesigner.Runtime.dll`
is a game dep — plus nav) for low mechanical value. Revisit as polish.

## D4 — Digest & policy UI: smartphone messages + options panel (v1)

**Decision.** Weekly digest → smartphone messages via `Contact` / `TextMessage` (proven by the
BackAlleyDealer example). Policy knobs (restock budget cap, staffing level, leave approval mode,
training budget, price policy) → an `OptionsService` / `ModOptions` panel (`AddHeader` / `AddToggle`
/ `AddSlider` / `AddDropdown` — all shown in `ExampleOptionsLogic`). A bespoke native window is
deferred.

**Open sub-question for Phase 0.** Whether `ModOptions` can be scoped per-store or only globally.
If global-only, v1 ships one policy profile applied to every managed store; per-store overrides
come later via a small custom window.

## D5 — Scheduling: call the game's scheduler, don't reimplement

**Decision.** The manager's rostering delegates to the game's existing scheduling system rather
than computing shifts itself.

**Evidence.** `Google.OrTools.dll` (Google's constraint/optimization solver) is a shipped game
dependency — the game already models scheduling/logistics as an optimization problem. Reimplementing
would diverge from game behaviour and double the surface area.

**Consequence.** Phase 0 probe #2 ("write a roster entry") must locate that entry point in
`BigAmbitions.dll` / `BigAmbitions.AI.dll` and confirm a mod can invoke it with constraints
(who's available, target staffing level).

## D6 — Persistence: additive per-store blob on the game save

**Decision.** Persist a `StoreManagerData` record per managed store, keyed by mod id + store id,
written into the game save; removed cleanly on `OnUnloadAsync`.

**Evidence.** Game uses `OdinSerializer`; `SaveGameManager.Current` is reachable; the SDK's
save/restore-on-unload pattern is the established idiom.

**Open sub-question for Phase 0.** Exact mod-save API (a `ModContext` save hook vs. piggybacking
an existing container). Design assumes additive + reversible either way.

## D7 — Team Leaders / departments: out of scope for v1 and v2

**Decision.** No department (Frozen/Fresh/Produce) or Team Leader tier until the base mod ships
and the community asks.

**Evidence.** No department concept visible in the SDK surface or example mods; `BusinessType`
is the finest business-structure unit exposed. Building departments would be mod-invented from
scratch. StuartArmour flagged it as "a bit much" himself.

## D8 — Project lives as a standalone repo

`C:\Source\BigAmbitions-StoreManagerMod\`, structured so `mod/StoreManager/` drops straight into
`<SDK clone>/Assets/Mods/StoreManager/`. Not inside the HekWereldBlazor repo (unrelated).

## Wage ladder (placeholder numbers, for playtest tuning)

| Rank | Hourly | One-time hire fee (skill-scaled) |
|------|--------|----------------------------------|
| Employee | $16–18 | — |
| Team Leader | $20–23 | low |
| Assistant Manager | $24–27 | mid |
| Manager | $28–32 | high |
