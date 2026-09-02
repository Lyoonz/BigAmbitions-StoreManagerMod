# v3 Phase A — the one in-game test

Deployed: the v3 `StoreManager` mod (custom `sm:skill_storemanager` skill + Harmony) **and** the
headless `StoreManagerProbe` alongside it. One launch covers everything.

## 0. Launch

Start Big Ambitions normally (no `-console` needed). Load the **day-125 save**
(*Foundation Headquarters* + *The Signature Mart*).

Within ~10 s the probe runs headless and writes to
`AppData/LocalLow/Hovgaard Games/Big Ambitions/Player.log`. It:

- prints `V3 HarmonyBootstrap.Patched`, `RoleSystemState.State`, `GetData("sm:skill_storemanager")`,
  and the wage — this tells us the custom skill is actually live;
- runs the v2 supervision self-test with the custom skill (injects a throwaway manager, adopts,
  assigns a store, runs one weekly restock pass, restores, cleans up).

**After the probe, the throwaway employee is gone.** You do not need to avoid saving for the
manual steps below — but use a **fresh save slot** for step 4 so your real day-125 save is never
touched.

## 1. Check the skill is real (Settings → Mods → Store Manager)

Open **Esc → Settings → Mods → Store Manager**. The panel should NOT show
"the Store Manager role is disabled on this game build". If it does — stop, tell me the reason
line, that's the kill-switch firing.

## 2. Hire a Filiaalmanager

Panel → **Quick actions → "Hire a new Store Manager onto my HQ"**.
A toast confirms the hire and the hourly wage (~$30 expected).

Open **My Employees**. The new hire should list **"Filiaalmanager"** (nl) / "Store Manager" (en)
as their role — **not** "Purchasing Agent", not a raw `sm:skill_storemanager` id. This is the
whole point of Option 1. Tell me exactly what the row says.

## 3. Put them to work

- **My Employees → the manager → assign to your Headquarters** (if not already).
- Panel → **Store Manager** dropdown → pick them.
- Panel → tick **The Signature Mart** (it has a delivery contract).
- Panel → **Quick actions → "Run the weekly restock pass now"**.
- Watch for the toast + the "Store Manager" phone thread. Check **BizMan → The Signature Mart →
  Deliveries** — the contract amounts should have been tuned.

## 4. The reload test (this is the brick check)

- **Save to a new slot** (not day-125).
- **Quit to menu, load that new slot.**
- Does it load fully? Is the manager still there, still "Filiaalmanager", still supervising the
  store? If the load hangs or errors — that's the compat-fix NPE the Harmony prefix is meant to
  stop; tell me.

## 5. Safe uninstall (optional, verifies the exit path)

Panel → **Quick actions → "Safe uninstall — re-skill managers to Purchasing Agent"**.
Check My Employees — the manager should now read **"Purchasing Agent"**. Save, and it would then
be safe to delete the mod folder.

## What to send me

- The `[SKILLPROBE]` block from `Player.log` (especially the `V3 ...` lines).
- Any `[StoreManager]` lines that say `THREW`, `NPE`, `disabled`, or `Harmony patch incomplete`.
- What the My Employees row said in step 2.
- Whether step 4 reloaded cleanly.
