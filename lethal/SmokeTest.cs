using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LethalCraft;

// Explicit --lethalcraft-smoke only: exercise real game APIs and render actual game frames.
// Uses a separate save and a loopback-only LAN host. Never creates a Steam/public lobby.
internal sealed class SmokeTest : MonoBehaviour
{
    public static bool Active;
    string output=null!;
    float nextStatus;
    void Update()
    {
        if(!Active||Time.realtimeSinceStartup<nextStatus)return;
        nextStatus=Time.realtimeSinceStartup+10;
        Plugin.Log.LogInfo($"SMOKE: alive scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} ready={Plugin.Instance.Ready} status={Plugin.Instance.Status}");
    }
    IEnumerator Start()
    {
        Active=true;output=Environment.GetEnvironmentVariable("LETHALCRAFT_TEST_OUTPUT")??Path.Combine(BepInEx.Paths.PluginPath,"LethalCraft-test");
        Directory.CreateDirectory(output);Plugin.Log.LogInfo("SMOKE: starting local integration test");
        float deadline=Time.realtimeSinceStartup+60;
        PreInitSceneScript pre=null!;
        while(pre==null&&GameNetworkManager.Instance==null&&Time.realtimeSinceStartup<deadline){pre=FindObjectOfType<PreInitSceneScript>();yield return null;}
        if(pre!=null){yield return new WaitForSecondsRealtime(3);pre.ChooseLaunchOption(false);Plugin.Log.LogInfo("SMOKE: selected LAN through game API");}
        MenuManager menu=null!;
        while(menu==null&&Time.realtimeSinceStartup<deadline){if(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name=="MainMenu")menu=FindObjectOfType<MenuManager>();yield return null;}
        if(menu==null||GameNetworkManager.Instance==null){Fail("Game menu did not initialize");yield break;}
        if(!GameNetworkManager.Instance.disableSteam){Fail("Test requires LAN mode; refusing to create an online lobby");yield break;}
        yield return new WaitForSecondsRealtime(1);
        bool slots=Array.IndexOf(Environment.GetCommandLineArgs(),"--lethalcraft-slots")>=0;
        bool guest=Array.IndexOf(Environment.GetCommandLineArgs(),"--lethalcraft-multiplayer-guest")>=0;
        bool multiplayer=guest||Array.IndexOf(Environment.GetCommandLineArgs(),"--lethalcraft-multiplayer-host")>=0;
        if(multiplayer)
        {
            GameNetworkManager.Instance.currentSaveFileName="LethalCraft_MultiplayerTest";
            if(guest)menu.StartAClient();else{menu.LAN_HostSetLocal();GameNetworkManager.Instance.StartHost();}
            yield return MultiplayerRegression.Run(Plugin.Instance,output,guest);
            yield return new WaitForSecondsRealtime(2);Application.Quit();yield break;
        }
        GameNetworkManager.Instance.currentSaveFileName=slots?"LethalCraft_SlotTest_A":"LethalCraft_SmokeTest";
        menu.LAN_HostSetLocal();GameNetworkManager.Instance.StartHost();Plugin.Log.LogInfo("SMOKE: starting loopback host");
        deadline=Time.realtimeSinceStartup+180;
        while((Plugin.LocalPlayer==null||!Plugin.Instance.Ready)&&Time.realtimeSinceStartup<deadline)yield return new WaitForSecondsRealtime(.2f);
        if(!Plugin.Instance.Ready){Fail("Minecraft bridge did not become ready: "+Plugin.Instance.Status);yield break;}
        yield return new WaitForSecondsRealtime(2);
        var plugin=Plugin.Instance;var player=Plugin.LocalPlayer!;Vector3 before=player.transform.position;
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"--lethalcraft-combat")>=0)
        {yield return CombatRegression.Run(plugin,output);yield return new WaitForSecondsRealtime(2);Application.Quit();yield break;}
        if(slots){yield return SlotRegression.Run(plugin,output);yield return new WaitForSecondsRealtime(2);Application.Quit();yield break;}
        Plugin.Log.LogInfo($"SMOKE: READY mc={plugin.Minecraft.X},{plugin.Minecraft.Y},{plugin.Minecraft.Z} lc={before}");
        yield return RegressionTest.InputChecks(plugin,output);
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"01-connected.png"));
        plugin.Link.Input(1,26,1);yield return new WaitForSecondsRealtime(.5f);plugin.Link.Input(1,26,0);
        yield return new WaitForSecondsRealtime(.5f);Vector3 after=player.transform.position;
        float displacement=Vector3.Distance(before,after);Plugin.Log.LogInfo($"SMOKE: MOVEMENT distance={displacement} from={before} to={after}");
        plugin.Link.Input(1,8,1);plugin.Link.Input(1,8,0);yield return new WaitForSecondsRealtime(1);
        bool inventory=plugin.Minecraft.MenuOpen;
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"02-inventory.png"));
        plugin.Link.Input(1,41,1);plugin.Link.Input(1,41,0);yield return new WaitForSecondsRealtime(.5f);
        player.TeleportPlayer(before);plugin.Yaw=0;plugin.Pitch=40;
        plugin.Link.Input(1,34,1);plugin.Link.Input(1,34,0);yield return new WaitForSecondsRealtime(2);
        plugin.Link.Input(2,3,1);yield return new WaitForSecondsRealtime(.1f);plugin.Link.Input(2,3,0);
        yield return new WaitForSecondsRealtime(2);plugin.Pitch=25;
        yield return new WaitForSecondsRealtime(1);yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"03-blocks.png"));
        Plugin.Log.LogInfo("SMOKE: renderer "+plugin.world.Diagnostic());
        var boxes=plugin.world.BlockColliders();int placed=0;Vector3 block=default;
        foreach(var box in boxes)if(box.name=="Minecraft blocks"){placed++;block=box.transform.position;Plugin.Log.LogInfo($"SMOKE: block bounds {box.bounds}");}
        Vector3 delta=block-player.gameplayCamera.transform.position;
        if(placed>0){plugin.Yaw=Mathf.Atan2(delta.x,delta.z)*Mathf.Rad2Deg;plugin.Pitch=-Mathf.Atan2(delta.y,new Vector2(delta.x,delta.z).magnitude)*Mathf.Rad2Deg;}
        yield return new WaitForSecondsRealtime(1);
        plugin.Link.Input(2,1,1);yield return new WaitForSecondsRealtime(5);plugin.Link.Input(2,1,0);
        yield return new WaitForSecondsRealtime(1);int remaining=0;
        foreach(var box in plugin.world.BlockColliders())if(box.name=="Minecraft blocks")remaining++;
        float ground=player.transform.position.y,maxJump=ground;
        plugin.Link.Input(1,44,1);yield return new WaitForSecondsRealtime(.1f);plugin.Link.Input(1,44,0);
        float until=Time.realtimeSinceStartup+1.5f;while(Time.realtimeSinceStartup<until){maxJump=Mathf.Max(maxJump,player.transform.position.y);yield return null;}
        plugin.SetNativeMode(true);yield return new WaitForSecondsRealtime(.5f);bool released=!plugin.Possessed;
        plugin.SetNativeMode(false);yield return new WaitForSecondsRealtime(1);bool restored=plugin.Ready;
        float oldHealth=plugin.Minecraft.Health;player.DamagePlayer(25);yield return new WaitForSecondsRealtime(1);float hurtHealth=plugin.Minecraft.Health;
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"04-after-mining.png"));
        File.WriteAllText(Path.Combine(output,"result.txt"),$"bridgeReady={plugin.Ready}\ninventoryOpened={inventory}\nmovementDistance={displacement}\nplacedColliders={placed}\nminedColliders={placed-remaining}\njumpHeight={maxJump-ground}\nnativeReleased={released}\nnativeRestored={restored}\nhealthBefore={oldHealth}\nhealthAfterDamage={hurtHealth}\nnativeHealth={player.health}\nmcFrame={plugin.Minecraft.Frame}\nstatus={plugin.Status}\n");
        var terminal=FindObjectOfType<Terminal>();terminal.BeginUsingTerminal();
        yield return new WaitForSecondsRealtime(2);bool terminalReleased=!plugin.Possessed&&player.inTerminalMenu;
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"05-terminal.png"));
        terminal.QuitTerminal();yield return new WaitForSecondsRealtime(2);
        File.AppendAllText(Path.Combine(output,"result.txt"),$"terminalReleased={terminalReleased}\nterminalRestored={plugin.Ready}\n");
        var round=StartOfRound.Instance;round.StartGame();Plugin.Log.LogInfo("SMOKE: landing on moon");
        deadline=Time.realtimeSinceStartup+180;
        while(!round.shipHasLanded&&Time.realtimeSinceStartup<deadline)yield return new WaitForSecondsRealtime(.25f);
        yield return new WaitForSecondsRealtime(3);
        File.AppendAllText(Path.Combine(output,"result.txt"),$"moonLanded={round.shipHasLanded}\nmoonBridgeReady={plugin.Ready}\nmoonPosition={player.transform.position}\n");
        plugin.Pitch=0;
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"06-moon.png"));
        var entrance=FindObjectsOfType<EntranceTeleport>().FirstOrDefault(e=>e.isEntranceToBuilding&&e.entranceId==0);
        if(entrance!=null)
        {
            player.TeleportPlayer(entrance.entrancePoint.position);player.isInsideFactory=false;player.isInHangarShipRoom=false;player.isInElevator=false;
            plugin.Yaw=entrance.entrancePoint.eulerAngles.y;plugin.Pitch=10;
            yield return new WaitForSecondsRealtime(4);
            Vector3 outdoor=player.transform.position;
            float sampleStart=Time.realtimeSinceStartup;int sampleFrames=Time.frameCount;
            yield return new WaitForSecondsRealtime(3);
            File.AppendAllText(Path.Combine(output,"regressions.txt"),$"outdoorFps={(Time.frameCount-sampleFrames)/(Time.realtimeSinceStartup-sampleStart):0.0}\ncollisionMaxCaptureMs={plugin.collisions.MaxCaptureMilliseconds:0.000}\ncollisionSourceTriangles={plugin.collisions.SourceTriangles}\ncollisionCandidateTriangles={plugin.collisions.CandidateTriangles}\n");
            yield return RegressionTest.SprintChecks(plugin,output);
            player.TeleportPlayer(outdoor);yield return new WaitForSecondsRealtime(1);
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"07-outdoors.png"));
            entrance.TeleportPlayer();yield return new WaitForSecondsRealtime(5);
            File.AppendAllText(Path.Combine(output,"result.txt"),$"outdoorPosition={outdoor}\ninteriorTransition={player.isInsideFactory}\ninteriorBridgeReady={plugin.Ready}\ninteriorPosition={player.transform.position}\n");
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"08-interior.png"));
            plugin.Link.Input(1,34,1);plugin.Link.Input(1,34,0);plugin.Pitch=45;
            for(int direction=0;direction<4&&plugin.world.BlockColliders().Length==0;direction++)
            {
                plugin.Yaw=direction*90;yield return new WaitForSecondsRealtime(.5f);
                plugin.Link.Input(2,3,1);yield return new WaitForSecondsRealtime(.1f);plugin.Link.Input(2,3,0);yield return new WaitForSecondsRealtime(1);
            }
            File.AppendAllText(Path.Combine(output,"result.txt"),$"interiorPlacedColliders={plugin.world.BlockColliders().Length}\n");
            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"08-interior-block.png"));
            var type=Resources.FindObjectsOfTypeAll<EnemyType>().FirstOrDefault(e=>e.enemyName=="Hoarding bug");
            if(type!=null)
            {
                // Allow a separate Minecraft tick after the earlier block-slot selection.
                yield return new WaitForSecondsRealtime(.3f);
                plugin.Link.Input(1,30,1);yield return new WaitForSecondsRealtime(.1f);plugin.Link.Input(1,30,0);
                yield return new WaitForSecondsRealtime(.5f);
                Vector3 spawn=player.transform.position-player.transform.forward*1.5f;
                if(UnityEngine.AI.NavMesh.SamplePosition(spawn,out var nav,3,UnityEngine.AI.NavMesh.AllAreas))spawn=nav.position;
                var reference=RoundManager.Instance.SpawnEnemyGameObject(spawn,0,-1,type);
                if(reference.TryGet(out var networkObject))
                {
                    var enemy=networkObject.GetComponent<EnemyAI>();yield return new WaitForSecondsRealtime(1);enemy.SetEnemyStunned(true,15,player);
                    yield return new WaitForSecondsRealtime(.2f);int hp=enemy.enemyHP;
                    yield return RegressionTest.ArrowCheck(plugin,enemy,output);
                    foreach(var collider in enemy.GetComponentsInChildren<Collider>())Plugin.Log.LogInfo($"SMOKE: enemy collider {collider.name} {collider.bounds}");
                    Plugin.Log.LogInfo($"SMOKE: enemy at {enemy.transform.position}; player {player.transform.position}");
                    for(int attempt=0;attempt<4&&!enemy.isEnemyDead;attempt++)
                    {
                        enemy.SetEnemyStunned(true,5,player);
                        Vector3 aim=CombatBridge.BodyBounds(enemy).center-player.gameplayCamera.transform.position;
                        plugin.Yaw=Mathf.Atan2(aim.x,aim.z)*Mathf.Rad2Deg;plugin.Pitch=-Mathf.Atan2(aim.y,new Vector2(aim.x,aim.z).magnitude)*Mathf.Rad2Deg;
                        yield return new WaitForSecondsRealtime(.3f);plugin.Link.Input(2,1,1);yield return new WaitForSecondsRealtime(.1f);plugin.Link.Input(2,1,0);
                        yield return new WaitForSecondsRealtime(.8f);
                    }
                    File.AppendAllText(Path.Combine(output,"result.txt"),$"enemy={type.enemyName}\nenemyHpBefore={hp}\nenemyHpAfter={enemy.enemyHP}\nenemyKilled={enemy.isEnemyDead}\n");
                    yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"09-combat.png"));
                }
            }
        }
        Plugin.Log.LogInfo("SMOKE: COMPLETE; inspect captured frames before accepting rendering");
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"--lethalcraft-smoke-exit")>=0){yield return new WaitForSecondsRealtime(3);Application.Quit();}
    }
    void Fail(string reason)
    {
        File.WriteAllText(Path.Combine(output,"result.txt"),"FAILED: "+reason);Plugin.Log.LogError("SMOKE: "+reason);
        ScreenCapture.CaptureScreenshot(Path.Combine(output,"failure.png"));
    }
}
