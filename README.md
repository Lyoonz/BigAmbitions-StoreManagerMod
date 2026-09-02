# Store Manager — a Big Ambitions mod

Adds a **new native employee role: the Store Manager**. Hire one at your Headquarters — through
the normal Recruitment Agency, just like a Purchasing Agent or HR Manager — and assign them a few
of your shops. Each week they top up those shops' **wholesale delivery contracts** to keep the
shelves stocked, staying inside a weekly budget you set.

It behaves like a first-party HQ role: its own skill, its own wage (~$28–40/h scaling with skill),
its own **"Store Managers" tab** in the BizMan HQ screen, and a counter on the HQ card.

> Status: **first public test build (v0.1.0).** The Store Manager role is complete. A second role
> (Team Leader / departments) is planned. Feedback very welcome — see the bottom of this file.

---

## Install

1. Quit Big Ambitions.
2. Download `StoreManager-vX.Y.Z.zip` from the [Releases page](../../releases).
3. Extract it so you get this folder:
   ```
   %USERPROFILE%\AppData\LocalLow\Hovgaard Games\Big Ambitions\ModsLocal\StoreManager\
       StoreManager.dll
       Locales\  (en.json, nl.json)
       Dependencies\  (0Harmony.dll + MonoMod/Cecil)
   ```
   (`AppData` is hidden — paste the path into the Explorer address bar.)
4. Start the game. Options → Mods should list **Store Manager**.

**Harmony note:** the mod bundles Harmony 2.10.2 in `Dependencies\`. If you run another mod that
also bundles Harmony (e.g. *Lower Installation Fee*), that's fine — the game loads one shared copy.

## How to use

1. **Rent a Headquarters** if you don't have one.
2. **Recruit a Store Manager.** Phone → Recruitment Agency → pick your HQ as the business →
   the skill list now includes **"Store Manager"** → run the campaign → hire a candidate from
   My Employees → Candidates.
3. **Schedule them at the HQ.** My Employees → the manager → assign to your Headquarters, then
   BizMan → HQ → Schedule → give them a shift on a desk / laptop / computer (those now accept the
   Store Manager skill).
4. **Open BizMan → your HQ → the "Store Managers" tab.**
   - Pick the manager under **Make Store Manager**.
   - Tick the shops you want them to supervise (up to the skill cap: 1 shop at skill 20, up to 5
     near skill 100). Each shop **must already have a wholesale delivery contract**
     (BizMan → that shop → Deliveries) — the manager tunes an existing contract, it doesn't
     create one.
   - Per shop, set:
     - **Weekly budget $** — hard ceiling on that shop's delivery cost per week.
     - **Keep stock for … days** — how many days of sales to keep on the shelf. The manager
       orders toward this.
     - **Order extra (%)** — a safety margin added on top of every order (still capped by the
       budget).
5. Each in-game **Saturday** the manager recalculates next Monday's delivery for every supervised
   shop. You get a toast and a message from the "Store Manager" phone contact.

Global defaults for newly-assigned shops live in **Options → Mods → Store Manager**.

## Uninstall

Run **Options → Mods → Store Manager → Safe uninstall** first (it turns every Store Manager back
into a Purchasing Agent and clears the mod's plans). Save the game, then delete the
`ModsLocal\StoreManager\` folder.

If you delete the folder *without* running Safe uninstall while a Store Manager is hired, that
save can fail to load (the game can't find the custom skill). This is a normal modding risk —
just run Safe uninstall first.

## Compatibility & caveats

- Built and tested against **Big Ambitions Build 3672**. On a game update the mod runs a
  self-check; if the game changed shape it **disables itself safely** (supervision pauses, the tab
  hides, existing plans load read-only — nothing is lost) and tells you.
- Single-HQ tested. Multi-HQ / multi-city not tested.
- The mod never edits shared game data (business types, agency settings), so it won't make AI
  rivals hire Store Managers.

## Feedback

Please report bugs / ideas via Discord or the [Issues page](../../issues). Handy to include:
your Big Ambitions build number, what you did, and — if something threw — the
`AppData\LocalLow\Hovgaard Games\Big Ambitions\Player.log` (`[StoreManager]` lines).

## License

[MIT](LICENSE). Unofficial fan-made mod — not affiliated with Hovgaard Games. Bundled
dependencies (Harmony, MonoMod, Cecil) keep their own MIT licenses.

---

*Developer docs: `CONTINUE.md`, `DECISIONS.md`, `docs/DESIGN-v3.md`. Build from source:
`bash build/deploy-local.sh` (needs the .NET SDK + a local Big Ambitions install).*
