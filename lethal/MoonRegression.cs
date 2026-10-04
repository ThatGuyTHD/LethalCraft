using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LethalCraft;

internal static class MoonRegression
{
    static string output="",other="";
    static void Check(bool ok,string name)
    {File.AppendAllText(Path.Combine(output,"moons.txt"),(ok?"PASS ":"FAIL ")+name+"\n");if(!ok)throw new InvalidOperationException(name);}
    static IEnumerator Wait(Func<bool> condition,string name,float seconds=150)
    {float end=Time.realtimeSinceStartup+seconds;while(!condition()&&Time.realtimeSinceStartup<end)yield return new WaitForSecondsRealtime(.2f);Check(condition(),name);}
    static IEnumerator Snapshot(Plugin p,int code)
    {p.Link.Input(90,(ushort)code);yield return Wait(()=>File.Exists(Path.Combine(output,"moon-"+code+".txt")),"Minecraft snapshot "+code);}
    static string Read(int code)=>File.ReadAllText(Path.Combine(output,"moon-"+code+".txt"));
    static IEnumerator Phase(Plugin p,int code,bool guest)
    {
        if(guest)
        {
            yield return Wait(()=>File.Exists(Path.Combine(other,"phase-"+code)),"host phase "+code,240);
            int moon=int.Parse(File.ReadAllText(Path.Combine(other,"phase-"+code)));
            yield return Wait(()=>StartOfRound.Instance.currentLevelID==moon&&p.Ready,"guest followed native moon "+moon);
            yield return new WaitForSecondsRealtime(2);yield return Snapshot(p,code);
            Check(Read(code).Contains("count=23"),"guest inventory travels with player "+code);
            string block=code==61?"minecraft:air":code==62?"minecraft:emerald_block":"minecraft:diamond_block";
            Check(Read(code).Contains(block),"guest sees correct moon blocks "+code);
            File.WriteAllText(Path.Combine(output,"seen-"+code),"ready");
        }
        else
        {
            File.WriteAllText(Path.Combine(output,"phase-"+code),StartOfRound.Instance.currentLevelID.ToString());
            yield return Wait(()=>File.Exists(Path.Combine(other,"seen-"+code)),"guest verified phase "+code);
        }
    }
    static IEnumerator Route(Plugin p,int moon)
    {
        yield return Wait(()=>StartOfRound.Instance.CanChangeLevels(),"native routing available");
        StartOfRound.Instance.ChangeLevelServerRpc(moon,UnityEngine.Object.FindObjectOfType<Terminal>().groupCredits);
        yield return Wait(()=>StartOfRound.Instance.currentLevelID==moon&&p.Ready,"host dimension ready "+moon);
        yield return new WaitForSecondsRealtime(2);
    }
    public static IEnumerator Run(Plugin p,string directory,bool guest)
    {
        output=directory;other=Path.Combine(Path.GetDirectoryName(directory)!,guest?"host":"guest");
        yield return Wait(()=>p.Ready,"bridge ready",210);
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"--lethalcraft-moon-migration")>=0)
        {
            int initial=StartOfRound.Instance.currentLevelID,next=(initial+1)%StartOfRound.Instance.levels.Length;
            yield return Snapshot(p,66);
            Check(Read(66).Contains("dimension=minecraft:overworld")&&Read(66).Contains("oldMarker=Block{minecraft:emerald_block}"),"old shared Overworld and existing build are retained on the first moon");
            Check(Read(66).Contains("registeredMoonDimensions="+StartOfRound.Instance.levels.Length),"every native moon has a registered dimension");
            yield return Snapshot(p,60);yield return Route(p,next);yield return Snapshot(p,61);
            Check(Read(61).Contains("minecraft:air")&&!Read(61).Contains("chestCount="),"migration does not copy old builds onto another moon");
            yield return Route(p,initial);yield return Snapshot(p,63);
            Check(Read(63).Contains("dimension=minecraft:overworld")&&Read(63).Contains("chestCount=11")&&Read(63).Contains("oldMarker=Block{minecraft:emerald_block}"),"return preserves both the migrated build and new chest");
            Check(true,"legacy migration regression complete");yield break;
        }
        if(guest)
        {
            File.WriteAllText(Path.Combine(output,"joined"),"ready");
            foreach(int code in new[]{60,61,62,63})yield return Phase(p,code,true);
            File.WriteAllText(Path.Combine(output,"done"),"ready");yield break;
        }
        yield return Wait(()=>File.Exists(Path.Combine(other,"joined")),"guest joined",210);
        int first=StartOfRound.Instance.currentLevelID,second=(first+1)%StartOfRound.Instance.levels.Length;
        int port=(int)p.Minecraft.ServerPort;uint epoch=p.multiplayer.GuestEpoch;
        yield return Snapshot(p,60);yield return Phase(p,60,false);
        Vector3 marker=Coordinates.ToUnity(12.5,4.5,12.5);
        yield return Wait(()=>p.world.BlockColliders().Any(c=>c.bounds.Contains(marker)),"first moon build has native collision");
        StartOfRound.Instance.randomMapSeed++;
        yield return new WaitForSecondsRealtime(3);
        Check(p.world.BlockColliders().Any(c=>c.bounds.Contains(marker)),"new dungeon seed does not clear same-moon builds");
        yield return Route(p,second);yield return Snapshot(p,61);
        Check(Read(61).Contains("minecraft:air")&&!Read(61).Contains("chestCount="),"second moon has no first-moon blocks or chest");
        Check(Read(61).Contains("hostCount=13")&&Read(61).Contains("guestCount=23"),"both player inventories survive travel");
        yield return Phase(p,61,false);
        yield return Snapshot(p,62);yield return Phase(p,62,false);
        yield return Route(p,first);yield return Snapshot(p,63);
        Check(Read(63).Contains("minecraft:diamond_block")&&Read(63).Contains("chestCount=11"),"return restores first-moon build and chest");
        Check((int)p.Minecraft.ServerPort==port&&p.multiplayer.GuestEpoch==epoch,"moon travel keeps Minecraft server and relay connected");
        yield return Phase(p,63,false);
        yield return Wait(()=>File.Exists(Path.Combine(other,"done")),"guest checks complete");
        GameNetworkManager.Instance.SaveGame();yield return new WaitForSecondsRealtime(3);
        string world=p.saves.WorldName;GameNetworkManager.Instance.Disconnect();
        yield return Wait(()=>UnityEngine.SceneManagement.SceneManager.GetActiveScene().name=="MainMenu"&&UnityEngine.Object.FindObjectOfType<MenuManager>()!=null,"returned to menu");
        yield return new WaitForSecondsRealtime(2);
        GameNetworkManager.Instance.currentSaveFileName="LethalCraft_MoonTest";
        UnityEngine.Object.FindObjectOfType<MenuManager>().LAN_HostSetLocal();GameNetworkManager.Instance.StartHost();
        yield return new WaitForSecondsRealtime(3);yield return Wait(()=>p.Ready,"reopened saved campaign",210);
        Check(p.saves.WorldName==world,"save identity survived reopening");
        if(StartOfRound.Instance.currentLevelID!=first)yield return Route(p,first);
        yield return Snapshot(p,64);
        Check(Read(64).Contains("minecraft:diamond_block")&&Read(64).Contains("chestCount=11")&&Read(64).Contains("hostCount=13"),"first moon and inventory survive server restart");
        yield return Route(p,second);yield return Snapshot(p,65);
        Check(Read(65).Contains("minecraft:emerald_block")&&Read(65).Contains("chestCount=19"),"second moon survives server restart");
        Check(true,"moon regression complete");
    }
}
