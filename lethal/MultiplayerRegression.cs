using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LethalCraft;

// Only called from the explicit smoke-test launch path. Uses isolated save files.
internal static class MultiplayerRegression
{
    static string output="",other="";
    static void Note(string key,object value){File.AppendAllText(Path.Combine(output,"multiplayer.txt"),key+"="+value+"\n");Plugin.Log.LogInfo("MULTIPLAYER TEST: "+key+"="+value);}
    static void Mark(string key)=>File.WriteAllText(Path.Combine(output,key),"ready");
    static IEnumerator Wait(Func<bool> predicate,string label,float seconds=150)
    {
        float end=Time.realtimeSinceStartup+seconds;
        while(!predicate()&&Time.realtimeSinceStartup<end)yield return new WaitForSecondsRealtime(.2f);
        Note(label,predicate());
    }
    public static IEnumerator Run(Plugin plugin,string directory,bool guest)
    {
        output=directory;other=Path.Combine(Path.GetDirectoryName(directory)!,guest?"host":"guest");
        yield return Wait(()=>plugin.Ready,"bridgeReady",210);
        if(!plugin.Ready)yield break;
        Note("role",plugin.multiplayer.Role);Note("world",plugin.saves.WorldName);Mark("joined");
        if(!guest)
        {
            yield return Wait(()=>File.Exists(Path.Combine(other,"joined")),"guestJoined",210);
            if(!File.Exists(Path.Combine(other,"joined")))yield break;
            plugin.Link.Input(90,30,0);yield return Wait(()=>File.Exists(Path.Combine(output,"mc-30.txt")),"hostFixture");
            Plugin.LocalPlayer!.TeleportPlayer(Coordinates.ToUnity(-3.5,1,-14.5));
            yield return new WaitForSecondsRealtime(8);Mark("fixture-ready");
            yield return Wait(()=>File.Exists(Path.Combine(other,"actions-done")),"guestActions");
            plugin.Link.Input(90,34,0);yield return Wait(()=>File.Exists(Path.Combine(output,"mc-34.txt")),"serverReport");
            yield return new WaitForSecondsRealtime(1);
            Note("nativeRemoteHidden",StartOfRound.Instance.allPlayerScripts.Where(p=>p.isPlayerControlled&&p!=Plugin.LocalPlayer).All(p=>p.GetComponentsInChildren<Renderer>(true).All(r=>r.forceRenderingOff)));
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"host-shared-world.png"));
            Mark("host-checked");
            yield return Wait(()=>File.Exists(Path.Combine(other,"native-mode")),"guestNativeMode");
            yield return new WaitForSecondsRealtime(2);
            Note("nativeRemoteRestored",StartOfRound.Instance.allPlayerScripts.Any(p=>p.isPlayerControlled&&p!=Plugin.LocalPlayer&&p.GetComponentsInChildren<Renderer>(true).Any(r=>!r.forceRenderingOff)));
            plugin.Link.Input(90,35,0);yield return new WaitForSecondsRealtime(1);Mark("mode-checked");
            yield return Wait(()=>File.Exists(Path.Combine(other,"reconnected")),"guestReconnected",180);
            plugin.Link.Input(90,36,0);yield return Wait(()=>File.Exists(Path.Combine(output,"mc-36.txt")),"savedInventoryReport");
            yield return CombatHost(plugin);Mark("done");yield return new WaitForSecondsRealtime(5);
            plugin.saves.RequestSave();yield return new WaitForSecondsRealtime(2);GameNetworkManager.Instance.SaveGame();
        }
        else
        {
            yield return Wait(()=>File.Exists(Path.Combine(other,"fixture-ready")),"hostFixtureReady");
            Plugin.LocalPlayer!.TeleportPlayer(Coordinates.ToUnity(1.5,1,-14.5));
            yield return new WaitForSecondsRealtime(8);
            plugin.Link.Input(90,31,0);yield return Wait(()=>File.Exists(Path.Combine(output,"mc-31.txt")),"clientReport");
            plugin.Link.Input(90,32,0);yield return new WaitForSecondsRealtime(2);
            plugin.Link.Input(90,33,0);yield return new WaitForSecondsRealtime(2);
            plugin.Link.Input(90,34,0);yield return new WaitForSecondsRealtime(1);
            Note("positionDifference",Vector3.Distance(Plugin.LocalPlayer.transform.position,Coordinates.ToUnity(plugin.Minecraft.X,plugin.Minecraft.Y,plugin.Minecraft.Z)));
            Note("nativeRemoteHidden",StartOfRound.Instance.allPlayerScripts.Where(p=>p.isPlayerControlled&&p!=Plugin.LocalPlayer).All(p=>p.GetComponentsInChildren<Renderer>(true).All(r=>r.forceRenderingOff)));
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"guest-shared-world.png"));
            Mark("actions-done");yield return Wait(()=>File.Exists(Path.Combine(other,"host-checked")),"hostChecked");
            plugin.SetNativeMode(true);yield return new WaitForSecondsRealtime(2);Mark("native-mode");
            yield return Wait(()=>File.Exists(Path.Combine(other,"mode-checked")),"modeChecked");
            plugin.SetNativeMode(false);yield return new WaitForSecondsRealtime(1);
            plugin.Link.Input(90,37,0);yield return new WaitForSecondsRealtime(3);
            yield return Wait(()=>plugin.Ready,"rejoinedMinecraft",100);
            plugin.Link.Input(90,38,0);yield return Wait(()=>File.Exists(Path.Combine(output,"mc-38.txt")),"rejoinReport");
            Mark("reconnected");yield return CombatGuest(plugin);yield return Wait(()=>File.Exists(Path.Combine(other,"done")),"hostDone");
        }
        Note("sentBytes",plugin.multiplayer.BytesSent);Note("receivedBytes",plugin.multiplayer.BytesReceived);Note("complete",true);
    }
    static IEnumerator EnterMoon(Plugin plugin)
    {
        yield return Wait(()=>StartOfRound.Instance.shipHasLanded,"moonLanded",180);
        yield return new WaitForSecondsRealtime(3);
        var entrance=UnityEngine.Object.FindObjectsOfType<EntranceTeleport>().FirstOrDefault(e=>e.isEntranceToBuilding&&e.entranceId==0);
        if(entrance==null){Note("entranceMissing",true);yield break;}
        var player=Plugin.LocalPlayer!;player.TeleportPlayer(entrance.entrancePoint.position);player.isInsideFactory=false;player.isInHangarShipRoom=false;player.isInElevator=false;
        yield return new WaitForSecondsRealtime(1);entrance.TeleportPlayer();yield return new WaitForSecondsRealtime(5);
        Note("interior",player.isInsideFactory);Note("interiorReady",plugin.Ready);
    }
    static IEnumerator CombatHost(Plugin plugin)
    {
        StartOfRound.Instance.StartGame();yield return EnterMoon(plugin);Mark("landed");
        yield return Wait(()=>File.Exists(Path.Combine(other,"interior")),"guestInterior");
        var type=Resources.FindObjectsOfTypeAll<EnemyType>().First(e=>e.enemyName=="Hoarding bug");
        Vector3 position=Plugin.LocalPlayer!.transform.position-Plugin.LocalPlayer.transform.forward*2;
        if(UnityEngine.AI.NavMesh.SamplePosition(position,out var nav,4,UnityEngine.AI.NavMesh.AllAreas))position=nav.position;
        var reference=RoundManager.Instance.SpawnEnemyGameObject(position,0,-1,type);
        yield return new WaitForSecondsRealtime(1);
        if(!reference.TryGet(out var obj)){Note("enemySpawned",false);yield break;}
        var enemy=obj.GetComponent<EnemyAI>();enemy.enemyHP=10;enemy.SetEnemyStunned(true,45,Plugin.LocalPlayer);
        File.WriteAllText(Path.Combine(output,"enemy-id"),enemy.NetworkObjectId.ToString());
        bool flash=false;float end=Time.realtimeSinceStartup+45;
        while(!File.Exists(Path.Combine(other,"arrow-done"))&&Time.realtimeSinceStartup<end){flash|=enemy.GetComponent<HitFlash>()?.Showing==true;yield return null;}
        Note("guestArrowReplicatedDamage",enemy.enemyHP<10);Note("hostEnemyHpAfter",enemy.enemyHP);Note("hostRedFlash",flash);
        plugin.Link.Input(90,45);yield return new WaitForSecondsRealtime(2);Mark("shield-equipped");
        yield return Wait(()=>File.Exists(Path.Combine(other,"shield-done")),"guestShieldComplete");
        plugin.Link.Input(90,59);yield return new WaitForSecondsRealtime(1);
        plugin.Link.Input(90,46);Mark("tnt-started");
        yield return Wait(()=>File.Exists(Path.Combine(other,"tnt-done")),"guestTntComplete");
    }
    static IEnumerator CombatGuest(Plugin plugin)
    {
        yield return Wait(()=>File.Exists(Path.Combine(other,"landed")),"hostLanded",210);
        yield return EnterMoon(plugin);Mark("interior");
        yield return Wait(()=>File.Exists(Path.Combine(other,"enemy-id")),"enemyOffered");
        if(!File.Exists(Path.Combine(other,"enemy-id")))yield break;
        ulong id=ulong.Parse(File.ReadAllText(Path.Combine(other,"enemy-id")));
        EnemyAI? enemy=null;float end=Time.realtimeSinceStartup+10;
        while(enemy==null&&Time.realtimeSinceStartup<end){enemy=UnityEngine.Object.FindObjectsOfType<EnemyAI>().FirstOrDefault(e=>e.IsSpawned&&e.NetworkObjectId==id);yield return null;}
        if(enemy==null){Note("enemyMatched",false);yield break;}
        Note("enemyMatched",true);enemy.enemyHP=10;
        Vector3 position=enemy.transform.position+Vector3.right*2;
        if(UnityEngine.AI.NavMesh.SamplePosition(position,out var nav,3,UnityEngine.AI.NavMesh.AllAreas))position=nav.position;
        Plugin.LocalPlayer!.TeleportPlayer(position);yield return new WaitForSecondsRealtime(7);
        yield return RegressionTest.ArrowCheck(plugin,enemy,output);Mark("arrow-done");
        yield return Wait(()=>File.Exists(Path.Combine(other,"shield-equipped")),"shieldEquipped");
        plugin.Yaw=0;plugin.Pitch=0;yield return new WaitForSecondsRealtime(1);
        plugin.Link.Input(2,3,1);yield return new WaitForSecondsRealtime(.7f);
        float health=plugin.Minecraft.Health;
        Landmine.SpawnExplosion(Plugin.LocalPlayer.transform.position+Vector3.forward*2,false,0,5,30);
        yield return new WaitForSecondsRealtime(1);Note("guestShieldFront",$"{health}->{plugin.Minecraft.Health}");
        Landmine.SpawnExplosion(Plugin.LocalPlayer.transform.position-Vector3.forward*2,false,0,5,30);
        yield return new WaitForSecondsRealtime(1);Note("guestShieldBack",$"{health}->{plugin.Minecraft.Health}; native={Plugin.LocalPlayer.health}");
        plugin.Link.Input(2,3,0);Mark("shield-done");
        yield return Wait(()=>File.Exists(Path.Combine(other,"tnt-started")),"tntStarted");
        health=plugin.Minecraft.Health;yield return new WaitForSecondsRealtime(3);
        Note("guestTntDamage",$"{health}->{plugin.Minecraft.Health}; native={Plugin.LocalPlayer.health}");Mark("tnt-done");
    }
}
