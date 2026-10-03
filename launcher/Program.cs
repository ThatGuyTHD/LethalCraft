namespace LethalCraftLauncher;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string Arg(string key,string fallback=""){int index=Array.IndexOf(args,key);return index>=0&&index+1<args.Length?args[index+1]:fallback;}
        string root=Arg("--root",Settings.DefaultRoot);
        try
        {
            if(args.Contains("--install-game"))
            {
                SetupEngine.RequireGamesClosed();
                new SetupEngine(root,s=>File.AppendAllText(Path.Combine(root,"install-helper.log"),s+"\n")).InstallGame(Arg("--install-game")).GetAwaiter().GetResult();return 0;
            }
            if(args.Contains("--self-test")){SelfTest.Run(Arg("--self-test")).GetAwaiter().GetResult();return 0;}
            if(args.Contains("--install-only"))
            {
                var settings=Settings.Load(root);string game=Arg("--install-only");if(game.Length>0)settings.GameDirectory=game;
                new SetupEngine(root,s=>File.AppendAllText(Path.Combine(root,"install.log"),s+"\n")).Install(settings).GetAwaiter().GetResult();return 0;
            }
            if(args.Contains("--preview"))
            {
                using var form=new LauncherForm(root);form.ShowInTaskbar=false;form.Opacity=0;form.Show();Application.DoEvents();using var bitmap=new Bitmap(form.Width,form.Height);
                form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Arg("--preview"));return 0;
            }
            Application.Run(new LauncherForm(root));return 0;
        }
        catch(Exception e)
        {
            Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"last-error.txt"),e.ToString());
            if(!args.Contains("--self-test")&&!args.Contains("--install-only")&&!args.Contains("--install-game"))MessageBox.Show(e.Message,"LethalCraft",MessageBoxButtons.OK,MessageBoxIcon.Error);
            return 1;
        }
    }
}
