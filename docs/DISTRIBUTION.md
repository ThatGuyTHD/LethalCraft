# Distribution review

Reviewed 2026-10-03; updated for 0.2.3 and the privacy rebuilds of 0.2.1/0.2.2.

- This is a free, unofficial mod. The release contains the mod, installer, open-source runtime/components, source and documentation. No paid game files or extracted assets are included.
- Minecraft Java mods may be distributed subject to the [Minecraft EULA](https://www.minecraft.net/en-us/eula). A modded copy of the game itself may not be redistributed. Minecraft is downloaded through Prism's normal installation/account flow instead.
- The [Minecraft Usage Guidelines](https://www.minecraft.net/en-us/usage-guidelines) apply to public sharing too. The name does not use Minecraft as its main title, official logos are not used, and attribution and the non-affiliation disclaimer are provided. A public publisher contact method is still pending; chat and forum links do not meet that guideline.
- Steam and Microsoft account checks remain enabled in normal play. The offline integration fixture additionally requires Fabric's development environment and both explicit verification flags; it is not available through the released launcher.
- Lethal Company remains subject to the [Steam Subscriber Agreement](https://store.steampowered.com/subscriber_agreement/) and applicable publisher terms. This review did not establish an express, comprehensive mod-distribution permission from Zeekerss. Do not interpret this release as official approval or permission to redistribute the game.
- Third-party license texts and attribution are included. Downloaded tools retain their own notices. Source and release archives are checked for account files, credentials, personal paths, saved games and proprietary game binaries before publication.

This is a packaging and license review, not a legal opinion or a guarantee of clearance in every jurisdiction. The software license does not override the games' terms or third-party rights.

Publication status: private GitHub release while a public publisher contact address is pending. The public-sharing contact requirement is not marked complete.

The privacy audit found local build paths in older compiler metadata. Release binaries were rebuilt with debug symbols disabled and mapped build paths. The audit checks Git history, ZIP/JAR entries, compressed .NET bundle entries and embedded installer ZIP payloads; matches are reported by category and location without printing secrets. See `tools/privacy_audit.py`. An automated scan is not a guarantee against every possible kind of sensitive information.
