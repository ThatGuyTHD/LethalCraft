# LethalCraft

minecraft inside lethal company. build, mine and fight monsters with minecraft gear.

**[download the installer](https://github.com/ThatGuyTHD/LethalCraft/releases/latest)**

NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.
Not affiliated with Zeekerss either.

## how to play

You need Windows 10/11, Lethal Company installed through Steam, and a Minecraft Java Edition account that owns the game. Both games run at the same time.

1. Run `LethalCraft-Setup-0.2.2.exe` and pick your Lethal Company folder.
2. Press **Install / Update**. It sets up the mods and downloads missing tools.
3. Press **Minecraft account** and sign in through Prism: Settings → Accounts → Add Microsoft.
4. Press **Play**. It starts Minecraft and opens Lethal Company through Steam. Sign in to Steam if it asks.

Close both games before updating. Your saves stay there.

## multiplayer

Everyone needs the same LethalCraft version and their own copies of both games.

Choose **Online** for Steam friends or **LAN** for your local network. Host or join through Lethal Company. Minecraft joins the host's world automatically. For LAN, the host needs **Allow remote connections**.

If Host says Steam isn't connected, quit LC, sign in to Steam and press Play again. You can still use LAN while Steam is offline.

## controls

| key | what it does |
| --- | --- |
| E | interact with LC prompts or Minecraft blocks |
| I | inventory |
| right click | use item / raise shield |
| Tab / hold Alt | switch to / temporarily use LC controls |
| F5 | third person |
| F7 | toggle the helmet overlay; third person always hides it |
| F8 / F9 | toggle the bridge / diagnostics |

Shields block attacks from the front after you raise them. Hits from behind still hurt. Blocking uses shield durability. TNT and native explosions do damage. Scripted monster executions and unkillable enemies still follow LC's rules.

Minecraft builds and inventory stay with the LC save slot. Guests use the host's world with their own inventories. LC bodies and held items are hidden in Minecraft mode.

## still experimental

Two clients on local LAN have been tested. A real two-PC Steam session has not. Other mods and game updates can break things.

You can't mine native LC terrain. Minecraft builds share coordinates across moons within the same save. Separate dimensions for each moon aren't implemented.

[setup, saves and troubleshooting](docs/SETUP.md) · [build from source](docs/BUILDING.md) · [test results](docs/TESTING.md)

## credits

The Minecraft bridge is adapted from [chasmlol's SkyCraft](https://github.com/chasmlol/SkyCraft). LethalCraft adds the LC side, multiplayer relay and installer. The original MIT license and credits are included.

Published by **ThatGuyTHD**. Public contact details are pending; this repository is private for now.

[licenses and third-party notices](THIRD_PARTY_NOTICES.md) · [distribution review](docs/DISTRIBUTION.md)
