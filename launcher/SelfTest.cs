using System.IO.Compression;

namespace LethalCraftLauncher;

internal static class SelfTest
{
    public static async Task Run(string directory)
    {
        directory=Path.GetFullPath(directory);Directory.CreateDirectory(directory);
        string report=Path.Combine(directory,"result.txt");File.WriteAllText(report,"");
        void Check(bool ok,string name){File.AppendAllText(report,(ok?"PASS ":"FAIL ")+name+"\n");if(!ok)throw new Exception(name);}
        var settings=new Settings{GameDirectory=Path.Combine(directory,"FakeSteamGame")};Directory.CreateDirectory(settings.GameDirectory);
        File.WriteAllText(Path.Combine(settings.GameDirectory,"Lethal Company.exe"),"installer fixture - not a game executable");
        var engine=new SetupEngine(directory,s=>File.AppendAllText(Path.Combine(directory,"install.log"),s+"\n"));
        await engine.Install(settings,true);
        Check(File.Exists(settings.PrismExe)&&File.Exists(settings.JavaPath),"fresh setup installs pinned Prism and Java without global dependencies");
        Check(File.Exists(Path.Combine(settings.GameDirectory,"BepInEx","core","BepInEx.dll")),"fresh setup installs verified BepInEx");
        Check(File.ReadAllText(Path.Combine(settings.GameDirectory,"BepInEx","config","BepInEx.cfg")).Contains("HideManagerGameObject = true"),"required loader setting enabled");
        Check(File.Exists(Path.Combine(settings.Instance,".minecraft","mods",SetupEngine.BridgeJar)),"Minecraft bridge installed");
        string saves=Path.Combine(settings.Instance,".minecraft","saves","ExistingWorld");Directory.CreateDirectory(saves);File.WriteAllText(Path.Combine(saves,"keep.txt"),"player builds");
        File.WriteAllText(Path.Combine(settings.PrismRoot,"accounts.json"),"{\"accounts\":[]}");
        File.WriteAllText(Path.Combine(settings.Instance,".minecraft","mods","skycraft-0.1.2-lethalcraft.jar"),"old mod fixture");
        File.WriteAllText(Path.Combine(settings.GameDirectory,"BepInEx","config","local.lethalcraft.bridge.cfg"),"[Appearance]\nShowFirstPersonMask = false\n");
        await engine.Install(settings,true);
        Check(File.ReadAllText(Path.Combine(saves,"keep.txt"))=="player builds","repair preserves existing worlds");
        Check(File.ReadAllText(Path.Combine(settings.PrismRoot,"accounts.json"))=="{\"accounts\":[]}","repair preserves account data");
        Check(File.ReadAllText(Path.Combine(settings.GameDirectory,"BepInEx","config","local.lethalcraft.bridge.cfg")).Contains("false"),"repair preserves mask and save configuration");
        Check(Directory.GetFiles(Path.Combine(settings.Instance,".minecraft","mods"),"skycraft-*-lethalcraft.jar").Length==1&&Directory.GetFiles(Path.Combine(settings.Instance,"lethalcraft-backups"),"*.jar",SearchOption.AllDirectories).Length==1,"upgrade archives old bridge outside mods folder");
        Check(Settings.Load(directory).InstalledVersion==SetupEngine.Version&&!settings.HasAccount(),"settings persist and missing account detected");
        string bad=Path.Combine(directory,"bad.zip");using(var zip=ZipFile.Open(bad,ZipArchiveMode.Create)){using var writer=new StreamWriter(zip.CreateEntry("../escape.txt").Open());writer.Write("rejected");}
        bool rejected=false;try{SetupEngine.Extract(bad,Path.Combine(directory,"safe"));}catch(IOException){rejected=true;}
        Check(rejected&&!File.Exists(Path.Combine(directory,"escape.txt")),"archive path traversal rejected");
        File.AppendAllText(report,"10 installer checks passed. Game files in this fixture are placeholders; no game launch was simulated.\n");
    }
}
