using System.Diagnostics;

namespace LethalCraftLauncher;

internal sealed class LauncherForm:Form
{
    readonly string root;readonly Settings settings;
    readonly TextBox game=new(),log=new();readonly Label status=new();readonly ProgressBar progress=new();
    readonly Button install=new(),play=new(),account=new(),browse=new();bool busy;
    static readonly Color Background=Color.FromArgb(22,26,25),Surface=Color.FromArgb(33,40,37),Accent=Color.FromArgb(135,220,126),Muted=Color.FromArgb(174,189,178);
    public LauncherForm(string root)
    {
        this.root=root;settings=Settings.Load(root);
        Text="LethalCraft";ClientSize=new Size(720,650);MinimumSize=new Size(736,689);StartPosition=FormStartPosition.CenterScreen;
        BackColor=Background;ForeColor=Color.White;Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Dpi;
        Label TextAt(string text,int x,int y,int width,int height,float size,Color color)
        {var label=new Label{Text=text,Location=new Point(x,y),Size=new Size(width,height),Font=new Font("Segoe UI",size),ForeColor=color};Controls.Add(label);return label;}
        TextAt("LETHALCRAFT",30,24,560,44,25,Accent);
        TextAt("Minecraft + Lethal Company     /     0.2.1",32,73,650,25,11,Muted);
        TextAt("Lethal Company folder",32,121,500,25,10,Color.White);
        game.SetBounds(32,151,545,30);game.Text=settings.GameDirectory;game.BackColor=Surface;game.ForeColor=Color.White;game.BorderStyle=BorderStyle.FixedSingle;Controls.Add(game);
        ButtonAt(browse,"Browse…",590,147,99,36,false);browse.Click+=(_,_)=>{using var dialog=new OpenFileDialog{Title="Choose Lethal Company.exe",Filter="Lethal Company|Lethal Company.exe",CheckFileExists=true};if(dialog.ShowDialog(this)==DialogResult.OK)game.Text=Path.GetDirectoryName(dialog.FileName)!;};
        ButtonAt(install,settings.InstalledVersion.Length==0?"Install":"Install / Update",32,204,184,48,false);
        ButtonAt(account,"Minecraft account",229,204,217,48,false);
        ButtonAt(play,"Play",459,204,230,48,true);
        install.Click+=async(_,_)=>await Work(async()=>{settings.GameDirectory=game.Text.Trim().Trim('"');await new SetupEngine(root,Report).Install(settings);install.Text="Install / Update";});
        account.Click+=(_,_)=>{try{settings.OpenPrism();Report("In Prism, open Settings → Accounts → Add Microsoft. Sign in, then return here and press Play.");}catch(Exception e){Report(e.Message);}};
        play.Click+=async(_,_)=>await Work(Play);
        status.SetBounds(32,274,658,48);status.ForeColor=Accent;Controls.Add(status);
        progress.SetBounds(32,325,657,5);progress.Visible=false;progress.Style=ProgressBarStyle.Marquee;Controls.Add(progress);
        TextAt("Play together",32,349,250,25,12,Color.White);
        TextAt("Everyone installs LethalCraft. Choose Online for Steam friends, or LAN\nfor your local network. Host or join in Lethal Company; Minecraft follows.\nFor LAN, the host selects Allow remote connections.",32,380,665,64,10,Muted);
        TextAt("Requires your own Lethal Company and Minecraft Java Edition accounts.",32,450,660,24,9,Muted);
        log.SetBounds(32,487,657,46);log.Multiline=true;log.ReadOnly=true;log.ScrollBars=ScrollBars.Vertical;log.BackColor=Surface;log.ForeColor=Muted;log.BorderStyle=BorderStyle.None;log.Font=new Font("Consolas",8);Controls.Add(log);
        TextAt("NOT AN OFFICIAL MINECRAFT PRODUCT.\nNOT APPROVED BY OR ASSOCIATED WITH MOJANG OR MICROSOFT.\nAlso not affiliated with Zeekerss. LethalCraft by ThatGuyTHD.",32,548,660,55,8,Muted);
        var credits=new LinkLabel{Text="Credits, licenses and contact",Location=new Point(32,613),Size=new Size(660,24),LinkColor=Accent};
        credits.LinkClicked+=(_,_)=>Process.Start(new ProcessStartInfo("https://github.com/ThatGuyTHD/LethalCraft/blob/main/THIRD_PARTY_NOTICES.md"){UseShellExecute=true});Controls.Add(credits);
        Report(settings.InstalledVersion.Length>0?"Ready. Press Play to start both games.":"Install once, sign in to Minecraft, then play.");
        FormClosing+=(_,e)=>{if(busy){e.Cancel=true;Report("Please wait for the current installation or launch to finish.");}};
    }
    void ButtonAt(Button button,string text,int x,int y,int width,int height,bool primary)
    {
        button.Text=text;button.SetBounds(x,y,width,height);button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=primary?Accent:Color.FromArgb(74,94,80);
        button.BackColor=primary?Accent:Surface;button.ForeColor=primary?Background:Color.White;button.Cursor=Cursors.Hand;Controls.Add(button);
    }
    void Report(string message)
    {
        if(InvokeRequired){BeginInvoke(()=>Report(message));return;}
        status.Text=message;log.AppendText(message+Environment.NewLine);
        Directory.CreateDirectory(root);File.AppendAllText(Path.Combine(root,"launcher.log"),DateTimeOffset.Now.ToString("u")+" "+message+Environment.NewLine);
    }
    async Task Work(Func<Task> action)
    {
        if(busy)return;busy=true;install.Enabled=play.Enabled=account.Enabled=browse.Enabled=game.Enabled=false;progress.Visible=true;
        try{await action();}catch(Exception e){Report(e.Message);}
        finally{busy=false;install.Enabled=play.Enabled=account.Enabled=browse.Enabled=game.Enabled=true;progress.Visible=false;}
    }
    async Task Play()
    {
        settings.GameDirectory=game.Text.Trim().Trim('"');
        if(settings.InstalledVersion!=SetupEngine.Version)throw new IOException("Press Install first to set up this version.");
        string exe=Path.Combine(settings.GameDirectory,"Lethal Company.exe");
        foreach(string file in new[]{exe,Path.Combine(settings.GameDirectory,"BepInEx","plugins","LethalCraft","LethalCraft.dll"),Path.Combine(settings.Instance,".minecraft","mods",SetupEngine.BridgeJar)})
            if(!File.Exists(file))throw new IOException("Some game files are missing. Press Install / Update to repair them.");
        if(!settings.HasAccount()){settings.OpenPrism();Report("Sign in through Prism: Settings → Accounts → Add Microsoft. Then press Play again.");return;}
        settings.Save(root);
        if(!MinecraftRunning())
        {
            Report("Starting Minecraft. First launch downloads its game files; Prism may ask you to refresh your sign-in.");settings.OpenPrism(true);
            var until=DateTime.UtcNow.AddMinutes(10);
            while(!MinecraftRunning()&&DateTime.UtcNow<until)await Task.Delay(500);
            if(!MinecraftRunning())throw new IOException("Minecraft did not start. Check Prism for a download or sign-in message, then press Play again.");
        }
        if(!Process.GetProcessesByName("Lethal Company").Any())Process.Start(new ProcessStartInfo(exe){UseShellExecute=true,WorkingDirectory=settings.GameDirectory});
        Report("Both games started. Host or join through Lethal Company. E: interact · I: inventory · F7: mask.");
    }
    static bool MinecraftRunning()
    {try{using var marker=Mutex.OpenExisting(@"Local\LethalCraft_v1_minecraft");return true;}catch(WaitHandleCannotBeOpenedException){return false;}}
}
