# Test results

## 0.2.3 moon dimensions and intro credit

Tested locally on Windows on 2026-10-03, using isolated saves.

- Two LC processes and two Minecraft clients travelled between two native moons. Each moon kept its own blocks and chest contents (11 apples on the first, 19 on the second). The host and guest kept their separate carried inventories. Returning restored the first moon's build.
- Travel kept the same Minecraft server port and relay session. Both moons' builds, containers and the host inventory survived a server restart.
- A separate two-client run verified guest reconnect and retained inventory, native-body visibility, landing and interior travel. Guest arrows reduced native enemy HP 10 → 8 and flashed red on both sides. Front shields preserved health 20 → 20 with durability cost 7; rear blasts dealt 20 → 14. TNT dealt 14 → 4.387, with native health 22. The arrow target's contact colliders were disabled after the arrow check to keep later monster attacks out of the shield measurements.
- Changing the dungeon seed on the same moon retained the build's native collision. A genuine pre-update world copy retained its existing Overworld build on the first moon opened; another moon started empty. Returning retained both the old build and a new chest. All 13 installed native levels had registered dimensions.
- The intro screenshot showed `Made By ThatGuy` below the boot text, labelled LethalCraft and without covering the original text.
- 19 bridge checks, 7 relay checks, 5 launcher checks and 29 Java checks passed. All 10 installer checks passed for 0.2.3 and both privacy rebuilds of the older releases.
- The privacy audit checked Git history, release ZIP/JAR files, compressed .NET bundles and embedded installer payloads. Older releases contained local Windows build paths in compiler metadata; the rebuilt copies passed. A positive control against an original installer detected the paths in the outer executable, bundled launcher and embedded mod.
- Installed 0.2.3 locally. All 139 original save/config files matched the fresh backup after restoring test-modified general settings and archiving test saves. The Minecraft account file's hash was unchanged.

This verifies two local clients and selected moons, not a two-PC Steam session or every moon/mod combination. Existing shared builds cannot be assigned to their original moons retrospectively; the first moon opened after updating inherits them once.

## 0.2.2 Online hosting fix

Tested locally on Windows on 2026-10-03 with the installed Steam copy of LC.

- Reproduced the disconnected-Steam state by launching LC directly with Steam closed. Online → Host → Confirm displayed the new recovery message and did not start a host. The message was checked in a game screenshot. Native startup still logs its own Steam initialization exceptions in this deliberately invalid state.
- Started Steam and LC through Steam's app launch route. Steam initialized, and the real Online → Host → Confirm handlers created a friends-only Steam lobby and spawned the host player in the ship scene. No invitations were sent. This fixture used a separate LC save and did not start Minecraft.
- Five launcher checks passed: secondary library paths, missing Steam, an unregistered game, a mismatched mod folder and an invalid manifest path.
- Both mods and the installer built. The 18 bridge checks, 7 relay checks, 25 Java checks and 10 installer checks passed again.
- Installed EXE, DLL and JAR matched the release build. All 139 original save/config files matched the fresh backup after restoring the fixture's general-settings changes and archiving its test save.

This verifies native Steam host creation on this PC. A friend joining from another PC remains untested. The Minecraft relay and combat results below are from 0.2.1; their implementation did not change in 0.2.2.

## 0.2.1 combat and multiplayer

Tested locally on Windows on 2026-10-03. These are local development and LAN tests, not a two-PC Steam acceptance test.

## Live combat

The fixture runs real LC `DamagePlayer` / `Landmine.SpawnExplosion` calls and real Minecraft primed TNT, through the shared-memory bridge. Shield use goes through Minecraft's mouse input handler. Tests use an isolated save and fresh Minecraft directory.

| check | observed result |
| --- | --- |
| Native blast, shield raised, facing blast | health 20 → 20; shield durability consumed: 7 |
| Native blast from behind | 20 → 14; native health 70; no shield wear |
| Native blast, shield lowered | 20 → 14; native health 70 |
| Outside blast range | 20 → 20; no shield wear |
| Native blast inside instant-kill range, shield facing blast | 20 → 20; player alive; shield wear 21 |
| Native melee, raised shield facing enemy | 20 → 20; shield wear 7 |
| Native melee from behind | 20 → 14; no shield wear |
| Native melee, shield lowered | 20 → 14 |
| TNT near a native enemy | enemy HP 100 → 86 |
| Second TNT while host uses native controls | enemy HP 86 → 76 |
| TNT near a survival player | health 20 → 10.387; native health 52 |

The enemy reductions match one application of the Minecraft proxy damage and configured multiplier, including the carried fractional remainder. Explosions do not add a second native enemy splash-damage pass.

## Two-client LAN

Two LC processes and two Minecraft clients joined the same native LAN lobby and shared Minecraft world. Block placement/mining, separate inventories, reconnect, remote-avatar visibility, native-mode restoration, moon landing and interior travel passed.

Guest arrows reduced enemy HP 10 → 8, replicated to the host, and flashed red on both sides. A guest's front shield blocked the native blast (20 → 20); a rear blast dealt damage (20 → 14, native 70). The host's Minecraft server reported guest health 14 and shield wear 7. TNT then reduced guest health 14 → 4.387, native 22.

The local two-client fixture uses test identities in a Fabric development environment. It does not prove authentication or Steam networking across separate PCs. Normal released play keeps Minecraft authentication enabled.

## Automated checks and installer

- 18 shared-memory checks and 7 relay checks passed, including simultaneous multi-megabyte transfers, backpressure, stream ordering and half-close.
- 25 Java collision, projectile and save-migration checks passed.
- A clean source checkout built both mods. The test apphost executable stalled on this PC; the identical test DLL passed using the .NET host. The test project now uses that direct path (`UseAppHost=false`). The root cause of the apphost stall was not established.
- All 10 installer checks passed: fresh setup, repair, save/account/config preservation, old-mod archiving, missing-account detection and archive traversal rejection. Results are in `verification/`. The launcher preview was checked for visible, unclipped controls and notices.
- All 138 original save files matched their pre-test SHA-256 hashes after restoring the native general-settings file changed by the fixture. Test-created native saves were archived outside the user's save folder.

Remaining limits: no real two-PC Steam session, four-player session, exhaustive enemies/moons, shield-breaking edge cases, every explosion source, or arbitrary mod combinations. The live melee fixture uses the same native damage entry point as enemy attacks; it does not test every monster's attack animation. Scripted executions remain native behavior.
