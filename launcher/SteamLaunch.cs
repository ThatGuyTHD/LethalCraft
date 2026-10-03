using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LethalCraftLauncher;

internal static class SteamLaunch
{
    internal const string GameUri="steam://run/1966720";
    internal static string Root=>Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam","SteamPath",null) as string
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam");

    // Read Steam's installed-app manifest, rather than accepting an unrelated copy of the EXE.
    internal static string FindGame(string steamRoot)
    {
        var libraries=new List<string>{steamRoot};
        string folders=Path.Combine(steamRoot,"steamapps","libraryfolders.vdf");
        if(File.Exists(folders))
            foreach(Match m in Regex.Matches(File.ReadAllText(folders),"\"path\"\\s+\"([^\"]+)\""))
                libraries.Add(m.Groups[1].Value.Replace(@"\\",@"\"));
        foreach(string library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string manifest=Path.Combine(library,"steamapps","appmanifest_1966720.acf");
            if(!File.Exists(manifest))continue;
            string data=File.ReadAllText(manifest);
            if(!Regex.IsMatch(data,"\"appid\"\\s+\"1966720\""))continue;
            var folder=Regex.Match(data,"\"installdir\"\\s+\"([^\"]+)\"");
            if(!folder.Success||folder.Groups[1].Value.IndexOfAny(new[]{'/', '\\', ':'})>=0||folder.Groups[1].Value is "." or "..")continue;
            string game=Path.Combine(library,"steamapps","common",folder.Groups[1].Value);
            if(File.Exists(Path.Combine(game,"Lethal Company.exe")))return Path.GetFullPath(game);
        }
        return "";
    }

    internal static void Validate(string gameDirectory,string steamRoot)
    {
        if(!File.Exists(Path.Combine(steamRoot,"steam.exe")))throw new IOException("Install Steam and sign in to the account that owns Lethal Company, then press Play again.");
        string registered=FindGame(steamRoot);
        if(registered.Length==0)throw new IOException("Steam has no installed Lethal Company entry. Install it through your Steam library, then select its folder here.");
        if(!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameDirectory)),Path.TrimEndingDirectorySeparator(registered),StringComparison.OrdinalIgnoreCase))
            throw new IOException("Steam launches a different Lethal Company folder. Select this folder and press Install / Update: "+registered);
    }

    internal static bool GameRunning()
    {
        var processes=Process.GetProcessesByName("Lethal Company");
        try{return processes.Length>0;}finally{foreach(var process in processes)process.Dispose();}
    }

    internal static async Task Start(Action<string> report)
    {
        if(GameRunning())return;
        report("Starting Lethal Company through Steam. Sign in to Steam if it asks.");
        using var started=Process.Start(new ProcessStartInfo(GameUri){UseShellExecute=true});
        var elapsed=Stopwatch.StartNew();
        while(!GameRunning()&&elapsed.Elapsed<TimeSpan.FromMinutes(2))await Task.Delay(500);
        if(!GameRunning())throw new IOException("Steam has not started Lethal Company. Finish signing in or updating in Steam, then press Play again.");
    }
}
