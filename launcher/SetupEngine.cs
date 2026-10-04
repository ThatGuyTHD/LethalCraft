using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LethalCraftLauncher;

internal sealed class SetupEngine
{
    public const string Version="0.2.4",BridgeJar="skycraft-0.2.4-lethalcraft.jar",ApiJar="fabric-api-0.161.0+26.3.jar";
    internal const string PrismUrl="https://github.com/PrismLauncher/PrismLauncher/releases/download/11.1.1/PrismLauncher-Windows-MinGW-w64-Portable-11.1.1.zip";
    internal const string PrismHash="05841d0b3bfc0a8212658457cc6035e1c29cd7ffae919464e874e384cbe8d062";
    internal const string JavaUrl="https://github.com/adoptium/temurin25-binaries/releases/download/jdk-25.0.4.1%2B1/OpenJDK25U-jre_x64_windows_hotspot_25.0.4.1_1.zip";
    internal const string JavaHash="4c95451cea98556def2c54f7782933f52a26d4a36bd85e1d59f0364464828b07";
    internal const string BepUrl="https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip";
    internal const string BepHash="82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4";
    readonly string root;readonly Action<string> report;
    readonly HttpClient http=new(){Timeout=TimeSpan.FromMinutes(15)};
    public SetupEngine(string root,Action<string> report){this.root=Path.GetFullPath(root);this.report=report;}
    internal static string Hash(string file){using var stream=File.OpenRead(file);return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();}
    internal async Task<string> Download(string name,string url,string expected)
    {
        string directory=Path.Combine(root,"Cache");Directory.CreateDirectory(directory);string file=Path.Combine(directory,name+".zip");
        if(File.Exists(file)&&Hash(file)==expected)return file;
        report("Downloading "+name+"…");
        using var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead);response.EnsureSuccessStatusCode();
        using(var input=await response.Content.ReadAsStreamAsync())
        using(var output=File.Create(file+".partial"))
        {
            byte[] buffer=new byte[131072];long count=0,last=0;int read;
            while((read=await input.ReadAsync(buffer))>0)
            {
                await output.WriteAsync(buffer.AsMemory(0,read));count+=read;
                if(count-last>5*1024*1024){last=count;report("Downloading "+name+": "+(count/1048576)+" MB");}
            }
        }
        if(Hash(file+".partial")!=expected)throw new IOException(name+" checksum did not match. The download was not installed.");
        File.Move(file+".partial",file,true);return file;
    }
    internal static void Extract(string archive,string destination)
    {
        using var zip=ZipFile.OpenRead(archive);Extract(zip,destination);
    }
    static void Extract(ZipArchive zip,string destination)
    {
        string prefix=Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        Directory.CreateDirectory(prefix);
        foreach(var entry in zip.Entries)
        {
            string target=Path.GetFullPath(Path.Combine(prefix,entry.FullName.Replace('/',Path.DirectorySeparatorChar)));
            if(!target.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new IOException("Archive contains an unsafe path.");
            if(entry.FullName.EndsWith('/')){Directory.CreateDirectory(target);continue;}
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);entry.ExtractToFile(target,true);
        }
    }
    internal string Payload()
    {
        string folder=Path.Combine(root,"Payload",Version);
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("LethalCraft.Payload")??throw new IOException("Installer payload missing.");
        using var zip=new ZipArchive(stream,ZipArchiveMode.Read);Extract(zip,folder);
        var hashes=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(Path.Combine(folder,"hashes.json")))!;
        foreach(var entry in hashes)if(Hash(Path.Combine(folder,entry.Key))!=entry.Value)throw new IOException("Bundled file failed verification: "+entry.Key);
        return folder;
    }
    public static void RequireGamesClosed()
    {
        if(Process.GetProcessesByName("Lethal Company").Any())throw new IOException("Save and close Lethal Company before installing this update.");
        try{using var marker=Mutex.OpenExisting(@"Local\LethalCraft_v1_minecraft");throw new IOException("Save and close LethalCraft's Minecraft window before installing this update.");}
        catch(WaitHandleCannotBeOpenedException){}
    }
    public async Task Install(Settings settings,bool fixture=false)
    {
        if(!fixture)RequireGamesClosed();
        if(!File.Exists(Path.Combine(settings.GameDirectory,"Lethal Company.exe")))throw new IOException("Choose the folder containing your installed Lethal Company.exe. Install the game through Steam first if needed.");
        string payload=Payload();
        if(!fixture&&settings.PrismExe.Length==0)
        {
            string existing=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","PrismLauncher","prismlauncher.exe");
            if(File.Exists(existing)){settings.PrismExe=existing;settings.PrismRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"PrismLauncher");}
        }
        if(settings.PrismExe.Length==0){settings.PrismExe=Path.Combine(root,"Prism","prismlauncher.exe");settings.PrismRoot=Path.Combine(root,"PrismData");}
        if(settings.PrismRoot.Length==0)throw new IOException("Prism data location is missing. Reset the LethalCraft launcher settings to set it up again.");
        if(Directory.Exists(settings.Instance)&&!File.Exists(Path.Combine(settings.Instance,"lethalcraft-managed.json")))throw new IOException("A separate Prism instance named LethalCraft already exists. Rename that instance in Prism before installing; it was left unchanged.");
        if(!File.Exists(settings.PrismExe))
        {
            string zip=await Download("Prism",PrismUrl,PrismHash);report("Installing Prism Launcher…");Extract(zip,Path.GetDirectoryName(settings.PrismExe)!);
        }
        if(settings.JavaPath.Length==0&&!fixture)
        {
            string existing=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"PrismLauncher","java","java-runtime-epsilon","bin","javaw.exe");
            if(File.Exists(existing))settings.JavaPath=existing;
        }
        if(!IsJava25(settings.JavaPath))
        {
            string zip=await Download("Java",JavaUrl,JavaHash);report("Installing Java 25…");Extract(zip,Path.Combine(root,"Java"));
            settings.JavaPath=Directory.GetFiles(Path.Combine(root,"Java"),"javaw.exe",SearchOption.AllDirectories).Single();
        }
        report("Installing the Minecraft mod…");InstallInstance(settings,payload);
        report("Installing the Lethal Company mod…");
        try{await InstallGame(settings.GameDirectory,payload);}
        catch(UnauthorizedAccessException)when(!fixture)
        {
            report("Windows needs permission to update the game's folder…");
            var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden};
            foreach(string arg in new[]{"--install-game",settings.GameDirectory,"--root",root})start.ArgumentList.Add(arg);
            using var helper=Process.Start(start)??throw new IOException("Windows did not start the installer.");await helper.WaitForExitAsync();
            if(helper.ExitCode!=0)throw new IOException("Game installation did not finish. See install-helper.log in "+root);
        }
        settings.InstalledVersion=Version;settings.Save(root);
        if(!fixture)InstallShortcut();
        report(settings.HasAccount()?"Installed. Press Play to start both games.":"Installed. Sign in to Minecraft in Prism, then press Play.");
    }
    internal static void InstallInstance(Settings settings,string payload)
    {
        var instance=settings.Instance;var mods=Path.Combine(instance,".minecraft","mods");Directory.CreateDirectory(mods);
        var backup=Path.Combine(instance,"lethalcraft-backups",DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));
        foreach(var old in Directory.EnumerateFiles(mods,"*.jar").Where(p=>(Path.GetFileName(p).StartsWith("skycraft-")&&p.EndsWith("-lethalcraft.jar")&&Path.GetFileName(p)!=BridgeJar)||(Path.GetFileName(p).StartsWith("fabric-api-")&&Path.GetFileName(p)!=ApiJar)).ToArray())
        {Directory.CreateDirectory(backup);File.Move(old,Path.Combine(backup,Path.GetFileName(old)));}
        foreach(string jar in new[]{BridgeJar,ApiJar})AtomicCopy(Path.Combine(payload,"mods",jar),Path.Combine(mods,jar));
        AtomicCopy(Path.Combine(payload,"prism","mmc-pack.json"),Path.Combine(instance,"mmc-pack.json"));
        string cfg=Path.Combine(instance,"instance.cfg");
        string text=File.Exists(cfg)?File.ReadAllText(cfg):File.ReadAllText(Path.Combine(payload,"prism","instance.cfg"));
        text=SetIni(text,"OverrideJavaLocation","true");text=SetIni(text,"JavaPath",settings.JavaPath.Replace('\\','/'));
        text=SetIni(text,"OverrideJavaArgs","true");text=SetIni(text,"JvmArgs","--enable-native-access=ALL-UNNAMED -Dskycraft.startHidden=true -Djavax.net.ssl.trustStoreType=Windows-ROOT -Djavax.net.ssl.trustStore=NONE");
        File.WriteAllText(cfg,text,new UTF8Encoding(false));
        string options=Path.Combine(instance,".minecraft","options.txt");
        if(!File.Exists(options))File.WriteAllText(options,"onboardAccessibility:false\nrenderDistance:6\nsimulationDistance:5\nmaxFps:120\npauseOnLostFocus:false\n");
        File.WriteAllText(Path.Combine(instance,"lethalcraft-managed.json"),JsonSerializer.Serialize(new{package="LethalCraft",version=Version}));
    }
    internal static string SetIni(string text,string key,string value)
    {
        string pattern="(?m)^"+Regex.Escape(key)+"=.*$";
        return Regex.IsMatch(text,pattern)?Regex.Replace(text,pattern,_=>key+"="+value):text.TrimEnd()+"\n"+key+"="+value+"\n";
    }
    static bool IsJava25(string path)
    {
        if(!File.Exists(path))return false;
        string release=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(path))!,"release");
        return File.Exists(release)&&Regex.IsMatch(File.ReadAllText(release),"(?m)^JAVA_VERSION=\"25(?:[.+\"])");
    }
    internal async Task InstallGame(string game,string? payload=null)
    {
        payload??=Payload();if(!File.Exists(Path.Combine(game,"Lethal Company.exe")))throw new IOException("Lethal Company.exe is missing.");
        string bep=Path.Combine(game,"BepInEx");
        if(!File.Exists(Path.Combine(bep,"core","BepInEx.dll")))
        {
            if(File.Exists(Path.Combine(game,"winhttp.dll")))throw new IOException("A different game loader is already installed. Use a clean Lethal Company installation or BepInEx 5.");
            var zip=await Download("BepInEx",BepUrl,BepHash);Extract(zip,game);
        }
        else if(AssemblyName.GetAssemblyName(Path.Combine(bep,"core","BepInEx.dll")).Version?.Major!=5)throw new IOException("LethalCraft requires BepInEx 5. Use a separate game installation for other loader versions.");
        string cfg=Path.Combine(bep,"config","BepInEx.cfg");Directory.CreateDirectory(Path.GetDirectoryName(cfg)!);
        string text=File.Exists(cfg)?File.ReadAllText(cfg):"[Chainloader]\n";
        if(File.Exists(cfg)&&!File.Exists(cfg+".pre-lethalcraft.bak"))File.Copy(cfg,cfg+".pre-lethalcraft.bak");
        if(Regex.IsMatch(text,@"(?m)^HideManagerGameObject\s*="))text=Regex.Replace(text,@"(?m)^HideManagerGameObject\s*=.*$","HideManagerGameObject = true");
        else if(text.Contains("[Chainloader]"))text=text.Replace("[Chainloader]","[Chainloader]\nHideManagerGameObject = true");
        else text+="\n[Chainloader]\nHideManagerGameObject = true\n";
        File.WriteAllText(cfg,text,new UTF8Encoding(false));
        string dll=Path.Combine(bep,"plugins","LethalCraft","LethalCraft.dll");Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
        if(File.Exists(dll)&&Hash(dll)!=Hash(Path.Combine(payload,"mods","LethalCraft.dll")))
        {string archive=Path.Combine(bep,"LethalCraftBackups",DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff"));Directory.CreateDirectory(archive);File.Copy(dll,Path.Combine(archive,"LethalCraft.dll"));}
        AtomicCopy(Path.Combine(payload,"mods","LethalCraft.dll"),dll);
    }
    internal static void AtomicCopy(string source,string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(source,destination+".lethalcraft-new",true);File.Move(destination+".lethalcraft-new",destination,true);
    }
    void InstallShortcut()
    {
        string installed=Path.Combine(root,"LethalCraft.exe"),current=Environment.ProcessPath!;
        if(!Path.GetFullPath(current).Equals(Path.GetFullPath(installed),StringComparison.OrdinalIgnoreCase))AtomicCopy(current,installed);
        // Shell links are generated through Windows Script Host; no shell commands are executed.
        var type=Type.GetTypeFromProgID("WScript.Shell");if(type==null)return;
        dynamic shell=Activator.CreateInstance(type)!;
        foreach(var folder in new[]{Environment.SpecialFolder.DesktopDirectory,Environment.SpecialFolder.Programs})
        {
            string path=Path.Combine(Environment.GetFolderPath(folder),"LethalCraft.lnk");dynamic shortcut=shell.CreateShortcut(path);
            shortcut.TargetPath=installed;shortcut.WorkingDirectory=root;shortcut.Description="LethalCraft — Minecraft + Lethal Company";shortcut.Save();
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
        }
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
    }
}
