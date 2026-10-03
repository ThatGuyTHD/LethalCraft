# 0.2.1 test results

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
