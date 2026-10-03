using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LethalCraftLauncher;

internal sealed class Settings
{
    public string GameDirectory { get; set; }="";
    public string PrismExe { get; set; }="";
    public string PrismRoot { get; set; }="";
    public string JavaPath { get; set; }="";
    public string InstalledVersion { get; set; }="";
    public static string DefaultRoot=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"LethalCraft");
    public static Settings Load(string root)
    {
        string path=Path.Combine(root,"settings.json");
        if(File.Exists(path))try{return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path))??new();}catch(JsonException){}
        return new Settings{GameDirectory=FindGame()};
    }
    public void Save(string root){Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"settings.json"),JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));}
    public static string FindGame()
    {
        var candidates=new List<string>();
        string steam=Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam","SteamPath",null) as string??@"C:\Program Files (x86)\Steam";
        candidates.Add(Path.Combine(steam,"steamapps","common","Lethal Company"));
        var libraries=Path.Combine(steam,"steamapps","libraryfolders.vdf");
        if(File.Exists(libraries))foreach(Match m in Regex.Matches(File.ReadAllText(libraries),"\"path\"\\s+\"([^\"]+)\""))
            candidates.Add(Path.Combine(m.Groups[1].Value.Replace(@"\\",@"\"),"steamapps","common","Lethal Company"));
        return candidates.FirstOrDefault(p=>File.Exists(Path.Combine(p,"Lethal Company.exe")))??"";
    }
    public string Instance=>Path.Combine(PrismRoot,"instances","LethalCraft");
    public bool HasAccount()
    {
        try
        {
            using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(PrismRoot,"accounts.json")));
            foreach(var account in document.RootElement.GetProperty("accounts").EnumerateArray())
                if(account.TryGetProperty("type",out var type)&&type.GetString()=="MSA"&&account.TryGetProperty("profile",out var profile)&&profile.TryGetProperty("id",out var id)&&!string.IsNullOrWhiteSpace(id.GetString()))return true;
        }catch(Exception e)when(e is IOException||e is JsonException||e is KeyNotFoundException||e is InvalidOperationException){}
        return false;
    }
    public void OpenPrism(bool launch=false)
    {
        if(!File.Exists(PrismExe))throw new IOException("Run Install first to set up Minecraft.");
        var start=new ProcessStartInfo(PrismExe){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(PrismExe)!};
        start.ArgumentList.Add("--dir");start.ArgumentList.Add(PrismRoot);
        if(launch){start.ArgumentList.Add("--launch");start.ArgumentList.Add("LethalCraft");}
        Process.Start(start);
    }
}
