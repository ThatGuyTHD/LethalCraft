using LethalCraftLauncher;

static class LauncherChecks
{
    internal static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"LethalCraft-launch-"+Guid.NewGuid().ToString("N"));
        string steam=Path.Combine(root,"Steam"),library=Path.Combine(root,"Other library");
        Directory.CreateDirectory(Path.Combine(steam,"steamapps"));
        Directory.CreateDirectory(Path.Combine(library,"steamapps","common","Lethal Company"));
        string game=Path.Combine(library,"steamapps","common","Lethal Company");
        void Check(bool ok,string description){if(!ok)throw new Exception(description);Console.WriteLine("PASS "+description);}
        bool Reject(Action action){try{action();return false;}catch(IOException){return true;}}
        try
        {
            File.WriteAllText(Path.Combine(game,"Lethal Company.exe"),"fixture");
            File.WriteAllText(Path.Combine(steam,"steam.exe"),"fixture");
            File.WriteAllText(Path.Combine(steam,"steamapps","libraryfolders.vdf"),"\"path\" \""+library.Replace(@"\",@"\\")+"\"");
            Check(Reject(()=>SteamLaunch.Validate(game,steam)),"unregistered game copy cannot launch a different Steam install");
            string manifest=Path.Combine(library,"steamapps","appmanifest_1966720.acf");
            File.WriteAllText(manifest,"\"AppState\" { \"appid\" \"1966720\" \"installdir\" \"Lethal Company\" }");
            SteamLaunch.Validate(game+Path.DirectorySeparatorChar,steam);
            Check(SteamLaunch.FindGame(steam)==game,"registered secondary Steam library with spaces is selected");
            Check(Reject(()=>SteamLaunch.Validate(root,steam)),"wrong mod destination is rejected before Steam starts");
            File.Delete(Path.Combine(steam,"steam.exe"));
            Check(Reject(()=>SteamLaunch.Validate(game,steam)),"missing Steam produces an actionable failure");
            File.WriteAllText(manifest,"\"appid\" \"1966720\" \"installdir\" \"../Elsewhere\"");
            Check(SteamLaunch.FindGame(steam)=="","manifest directory cannot escape the Steam common directory");
            Console.WriteLine("5 launcher checks passed.");
        }
        finally{Directory.Delete(root,true);}
    }
}
