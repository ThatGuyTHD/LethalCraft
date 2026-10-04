# Setup and saves

Use the EXE from GitHub Releases. It installs the LC plugin, a dedicated Prism instance and a desktop shortcut. Existing standard Prism installs are reused. Otherwise setup downloads a portable copy. Missing Java and BepInEx are installed automatically.

The installer is unsigned. Check the release's SHA-256 file against your download. If Windows blocks it, inspect the source or build it yourself; do not disable Windows security.

## Versions

Tested with LC Steam build 22825947 / game version 81, Unity 2022.3.62f2, BepInEx 5.4.23.5, Minecraft 26.3, Fabric Loader 0.19.5, Fabric API 0.161.0+26.3 and Java 25. Setup downloads Prism 11.1.1 and Temurin JRE 25.0.4.1+1 if needed. Protocol 16 requires matching 0.2.3 DLL and JAR versions on every PC.

## Where things go

- Launcher and settings: `%LOCALAPPDATA%/LethalCraft`.
- Existing Prism data: `%APPDATA%/PrismLauncher`. A newly installed portable Prism uses `PrismData` under the launcher folder.
- Minecraft instance: `instances/LethalCraft` under that Prism data folder.
- Worlds: the instance's `.minecraft/saves/LethalCraft_<slot>_<identity>` folders.
- LC plugin: `BepInEx/plugins/LethalCraft/LethalCraft.dll` in the game folder.
- LC config: `BepInEx/config/local.lethalcraft.bridge.cfg`.

LC stores a `LethalCraftWorldId` with each campaign. Switching slots switches the Minecraft world and inventory. Resetting a campaign creates a new identity and keeps the previous Minecraft folder. Back up both the native LC saves and the Minecraft saves together. Don't manually reuse identities between slots.

Each native moon index has a saved Minecraft dimension (`skycraft:moon_<index>`) within that campaign. Travelling moves the crew between dimensions without restarting the Minecraft server. Player inventories travel with them; blocks, containers and dropped items stay in their dimension. The Company building gets its own dimension too. Dimension registration uses the native level list, capped at 128 entries; changing a modded moon list's ordering can change which index names a moon.

For an older save, `lethalcraft-moons.txt` records which moon inherits the old shared Overworld. The first moon opened after updating is chosen once. Existing region and player files are retained in place; the other moons get empty dimensions. Keep this migration file with the world backup. An invalid migration record stops loading instead of silently sharing builds again.

Setup archives old bridge mods outside active mod folders and preserves worlds, accounts and gameplay settings. It sets BepInEx's required `HideManagerGameObject = true`.

## If something breaks

Open Prism and launch the LethalCraft instance to see download or Microsoft sign-in errors. Both games need the same Windows user and privilege level. Close both before installing updates.

Play starts LC through its registered Steam library entry. If Steam opens a sign-in or update screen, finish that step. The launcher checks that this is the folder containing your installed mod. Selecting a different game copy will ask you to install into the Steam folder instead.

If Online → Host previously did nothing, quit LC and use the updated launcher. Older launchers started the EXE directly and could leave Steam's lobby interface uninitialized. Online hosting now displays a recovery message when Steam isn't connected. LAN remains available without an online Steam connection.

Logs are `BepInEx/LogOutput.log`, the instance's `.minecraft/logs/latest.log` and the launcher's `launcher.log`. Remove private information before posting them.

F8 releases the bridge for ordinary native controls. To remove the mod, close both games and move `LethalCraft.dll` out of the plugins folder. Keep the Minecraft instance if you want its worlds.

`Combat.WeaponDamageMultiplier` controls Minecraft weapon damage against killable LC monsters. Default is 2. The helmet setting is saved when you press F7. A shield needs to be held up and facing the attack; it does not prevent scripted executions, drowning or other damage that vanilla shields do not block.

Native LC terrain cannot be mined. Partial Minecraft blocks use full-block native collision. Interiors can regenerate around existing builds on later days. Four-player sessions, all moons/enemies, high-latency Steam sessions and arbitrary mod combinations have not been tested.
