# Build

Install PowerShell 7, .NET SDK 8 and Java 25. You also need your own installed LC and BepInEx; the build references their DLLs locally without copying them into the release.

```powershell
./Build.ps1 -GameDirectory 'C:\Program Files (x86)\Steam\steamapps\common\Lethal Company' -JavaDirectory 'C:\path\to\jdk-25'
```

The script builds both mods, runs the bridge/relay and Java checks, fetches the pinned Fabric API JAR and builds the standalone installer under `dist`. The EXE contains its mod payload and .NET runtime. No .NET installation is needed on the player's PC.

`lethal` is the BepInEx adapter, `fabric` is the Minecraft mod adapted from SkyCraft, `launcher` is the Windows installer, and `protocol` documents shared memory. Some internal names still say SkyCraft or Skyrim for compatibility with the upstream code.

Game integration tests are opt-in: `--lethalcraft-smoke --lethalcraft-combat` or the multiplayer host/guest switches, paired with a Fabric development launch and `-Dlethalcraft.verify=true`. The two-client fixture additionally uses `-Dlethalcraft.testOffline=true`; that setting is ignored outside Fabric's development environment. Normal installed play uses Minecraft authentication. Test saves must be isolated and the user's games must be closed first.

GitHub excludes build output, game files, local test worlds, logs and the generated installer payload. The release ZIP includes the source but no decompiled game code or game libraries.

Before sharing a release, audit its full contents and Git history with Python 3:

```powershell
python tools/privacy_audit.py --git . path/to/LethalCraft-Setup-0.2.3.exe path/to/LethalCraft-0.2.3.zip --report audit.json
```

The scanner reports categories and locations without printing matched values. It expands ZIP/JAR archives, compressed .NET bundles and embedded installer payloads. It is one check alongside reviewing the files and images being shared, not a guarantee that every possible secret can be recognized. `Directory.Build.props` disables debug symbols and maps compiler paths so build machines' profile paths stay out of the binaries.
