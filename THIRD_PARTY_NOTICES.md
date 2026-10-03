# Credits and licenses

LethalCraft is published by ThatGuyTHD. Public contact details are pending; this repository is private for now.

NOT AN OFFICIAL MINECRAFT PRODUCT. NOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.
LethalCraft is also not affiliated with Zeekerss, Valve, Prism Launcher or the Fabric project.

The root [MIT license](LICENSE) covers this project's code, including its adapted SkyCraft code. It does not grant rights to Minecraft, Lethal Company, their assets or their trademarks.

## Included in the release

| component | license and source |
| --- | --- |
| SkyCraft Minecraft bridge and protocol, adapted for LC | Copyright 2026 chasmlol; [MIT](licenses/SkyCraft-LICENSE.txt); [original source at bfcaf178524b92c2cdeb88e4ce0f13ef9ded6f32](https://github.com/chasmlol/SkyCraft/tree/bfcaf178524b92c2cdeb88e4ce0f13ef9ded6f32). Changes include LC input, combat, rendering, save isolation and multiplayer. |
| Fabric API 0.161.0+26.3, unmodified | [Apache-2.0](licenses/Fabric-API-LICENSE.txt); [source](https://github.com/FabricMC/fabric/tree/26.3). The JAR and its nested module JARs retain their original license files. |
| .NET 8.0.31 runtime in the Windows executable | [MIT](licenses/DotNet-LICENSE.txt); [third-party notices](licenses/DotNet-THIRD-PARTY-NOTICES.txt); [runtime source](https://github.com/dotnet/runtime/tree/v8.0.31). |
| .NET Windows Desktop 8.0.31 in the executable | [MIT](licenses/DotNet-WindowsDesktop-LICENSE.txt); [source](https://github.com/dotnet/windowsdesktop/tree/v8.0.31). |
| Gradle 9.7.1 wrapper in the source package | [Apache-2.0](licenses/Gradle-LICENSE.txt); [source](https://github.com/gradle/gradle/tree/v9.7.1). |

## Downloaded from the original publishers

These are not bundled in the GitHub release. Setup downloads their unmodified official archives over HTTPS and checks pinned SHA-256 hashes before extraction. Their own notices remain in the installed archives.

| component | license and matching release/source |
| --- | --- |
| Prism Launcher 11.1.1 | [GPL-3.0](licenses/PrismLauncher-LICENSE.txt); [release](https://github.com/PrismLauncher/PrismLauncher/releases/tag/11.1.1); [source](https://github.com/PrismLauncher/PrismLauncher/tree/11.1.1). |
| Eclipse Temurin JRE 25.0.4.1+1 | GPL-2.0 with Classpath Exception, plus notices under `legal/` in the archive; [release and source links](https://github.com/adoptium/temurin25-binaries/releases/tag/jdk-25.0.4.1%2B1). |
| BepInEx 5.4.23.5 | [MIT](licenses/BepInEx-LICENSE.txt); [release](https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5); [source](https://github.com/BepInEx/BepInEx/tree/v5.4.23.5). Its archive includes other components with their own notices. |

Prism obtains Minecraft, Fabric Loader and game libraries from their respective services. Lethal Company must already be installed through Steam. Neither game, decompiled game code, game assets, accounts nor saved worlds are distributed here.

The upstream Skyrim plugin and its C++ dependencies are not used in LethalCraft. e4mc is not bundled or used; multiplayer uses the LC lobby relay.
