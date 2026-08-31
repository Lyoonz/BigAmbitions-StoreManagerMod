# Manifest & asmdef setup

`ModManifest.asset` in this folder ships with **placeholder GUIDs** — real Unity asset GUIDs
are generated on import and can't be authored blind.

On first open of the SDK project with this mod folder present:

1. If Unity shows `ModManifest.asset` as a broken script, just **delete it** and recreate:
   `Assets > Create > Big Ambitions/Mod Manifest`, place it at
   `Assets/Mods/StoreManager/ModManifest.asset`.
2. Fill the fields:
   | Field | Value |
   |-------|-------|
   | ModId | `StoreManager` (must equal the folder name) |
   | DisplayName | `Store Manager` |
   | Author | your name |
   | Version | `0.1.0` |
   | AssetBundleName | *(leave empty — no bundled assets in v1)* |
   | ModAssembly | drag `StoreManager.asmdef` |
   | LocalesFolder | drag the `Locales` folder |
   | DependenciesFolder | *(empty unless D2 kicks in — then the folder holding `0Harmony.dll`)* |
   | EnumsFile | `Scripts/enums.txt` once custom enum values are registered (e.g. a mod `CallDialogType` / contact category for the digest) |
   | TargetPlatforms | Windows + Mac |
3. `StoreManager.asmdef` is already correct — it's a byte-for-byte copy of the SDK's
   `Example-Options.asmdef` reference set (`overrideReferences: true`, all canonical game DLLs,
   `BA_GAME_DLLS_IMPORTED` constraint). `ModValidator` rule 6 ("Canonical Precompiled Drift")
   will confirm or offer to sync it.

`ModValidator` runs all 13 structural rules at build time — fix what it flags before uploading.
