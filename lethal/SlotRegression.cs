using System;
using System.Collections;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace LethalCraft;

// Opt-in fixture, always driven by SmokeTest in the isolated loopback/test instance.
internal static class SlotRegression
{
    const string A="LethalCraft_SlotTest_A", B="LethalCraft_SlotTest_B";
    static void Check(string output,string name,bool value)
    {
        File.AppendAllText(Path.Combine(output,"slots.txt"),name+"="+value+"\n");
        Plugin.Log.LogInfo("SLOTS: "+name+"="+value);
        if(!value)throw new InvalidOperationException("Slot regression failed: "+name);
    }
    static void Key(Plugin p,ushort code){p.Link.Input(1,code,1);p.Link.Input(1,code,0);}
    static IEnumerator Ready(Plugin p)
    {
        float end=Time.realtimeSinceStartup+120;
        while(!p.Ready&&Time.realtimeSinceStartup<end)yield return new WaitForSecondsRealtime(.1f);
        if(!p.Ready)throw new TimeoutException("Bridge not ready: "+p.Status);
        yield return new WaitForSecondsRealtime(1);
    }
    static IEnumerator Snapshot(Plugin p,string output,ushort code)
    {
        p.Link.Input(90,code);float end=Time.realtimeSinceStartup+10;
        while(!File.Exists(Path.Combine(output,"slot-"+code+".txt"))&&Time.realtimeSinceStartup<end)yield return null;
        if(!File.Exists(Path.Combine(output,"slot-"+code+".txt")))throw new TimeoutException("No Minecraft fixture reply "+code);
    }
    static string Read(string output,int code)=>File.ReadAllText(Path.Combine(output,"slot-"+code+".txt"));
    static IEnumerator Switch(Plugin p,string file,bool delete=false)
    {
        GameNetworkManager.Instance.Disconnect();
        float end=Time.realtimeSinceStartup+40;
        while((UnityEngine.SceneManagement.SceneManager.GetActiveScene().name!="MainMenu"||UnityEngine.Object.FindObjectOfType<MenuManager>()==null)&&Time.realtimeSinceStartup<end)yield return null;
        yield return new WaitForSecondsRealtime(1);
        if(p.saves.WorldName.Length!=0)throw new InvalidOperationException("Minecraft did not unload at main menu");
        if(delete)ES3.DeleteFile(file);
        var menu=UnityEngine.Object.FindObjectOfType<MenuManager>();
        GameNetworkManager.Instance.currentSaveFileName=file;
        menu.LAN_HostSetLocal();GameNetworkManager.Instance.StartHost();
        yield return new WaitForSecondsRealtime(1);yield return Ready(p);
    }
    public static IEnumerator Run(Plugin p,string output)
    {
        string firstWorld=p.saves.WorldName;
        Check(output,"legacyAssignedOnlyToA",p.saves.Flags==1&&firstWorld.Contains(A));
        File.AppendAllText(Path.Combine(output,"slots.txt"),"initialAWorld="+firstWorld+"\n");
        yield return AppearanceChecks(p,output);
        if(Array.IndexOf(Environment.GetCommandLineArgs(),"--lethalcraft-appearance-only")>=0){Plugin.Log.LogInfo("APPEARANCE: COMPLETE");yield break;}
        yield return Snapshot(p,output,20);
        GameNetworkManager.Instance.SaveGame();float end=Time.realtimeSinceStartup+20;
        while(p.Minecraft.SaveAck!=p.saves.SaveRequest&&Time.realtimeSinceStartup<end)yield return null;
        Check(output,"nativeSaveFlushAcknowledged",p.Minecraft.SaveAck==p.saves.SaveRequest);
        yield return Switch(p,B);
        Check(output,"slotBHasDifferentWorld",p.saves.WorldName!=firstWorld&&p.saves.Flags==0);
        yield return Snapshot(p,output,21);
        Check(output,"slotBHasNoABuildOrInventory",Read(output,21).Contains("minecraft:air")&&!Read(output,21).Contains("count=13"));
        Check(output,"oldBuildColliderRemoved",!p.world.BlockColliders().Any(c=>c.bounds.Contains(new Vector3(-12.5f,4.5f,12.5f))));
        yield return Snapshot(p,output,22);
        yield return Switch(p,A);
        Check(output,"slotAIdentityStable",p.saves.WorldName==firstWorld);
        yield return Snapshot(p,output,24);
        Check(output,"slotAReopensBuildAndInventory",Read(output,24).Contains("minecraft:diamond_block")&&Read(output,24).Contains("count=13"));
        yield return Switch(p,A,true);
        Check(output,"deletedSlotGetsNewIdentity",p.saves.WorldName!=firstWorld&&p.saves.Flags==0);
        yield return Snapshot(p,output,25);
        Check(output,"deletedSlotStartsFresh",Read(output,25).Contains("minecraft:air")&&!Read(output,25).Contains("count=13"));
        yield return Switch(p,B);
        yield return Snapshot(p,output,26);
        Check(output,"otherSlotSurvivesDeletion",Read(output,26).Contains("minecraft:emerald_block")&&Read(output,26).Contains("count=29"));
        string beforeReset=p.saves.WorldName;GameNetworkManager.Instance.ResetSavedGameValues();
        yield return new WaitForSecondsRealtime(.5f);yield return Ready(p);
        Check(output,"campaignResetGetsNewIdentity",p.saves.WorldName!=beforeReset);
        yield return Snapshot(p,output,28);
        Check(output,"campaignResetStartsFresh",Read(output,28).Contains("minecraft:air")&&!Read(output,28).Contains("count=29"));
        GameNetworkManager.Instance.Disconnect();yield return new WaitForSecondsRealtime(4);
        Check(output,"mainMenuUnloadsMinecraft",p.saves.WorldName.Length==0&&!p.Minecraft.InWorld);
        Plugin.Log.LogInfo("SLOTS: COMPLETE");
    }
    static IEnumerator AppearanceChecks(Plugin p,string output)
    {
        var player=Plugin.LocalPlayer!;
        while(p.Minecraft.CameraMode!=0){Key(p,62);yield return new WaitForSecondsRealtime(.4f);}
        if(!p.appearance.ShowFirstPersonMask)p.appearance.ToggleMask();
        yield return new WaitForSecondsRealtime(.3f);
        var visor=player.localVisor.GetComponentsInChildren<Renderer>(true);
        File.WriteAllLines(Path.Combine(output,"visor-renderers.txt"),visor.Select(r=>r.name+" enabled="+r.enabled+" off="+r.forceRenderingOff));
        Check(output,"firstPersonMaskVisible",visor.Length>0&&visor.Any(r=>r.enabled&&!r.forceRenderingOff));
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"01-mask-first-person.png"));
        Key(p,62);yield return new WaitForSecondsRealtime(.5f);
        Check(output,"thirdPersonRearMaskHidden",p.Minecraft.CameraMode==1&&p.appearance.MaskRendererCount>0&&visor.All(r=>r.forceRenderingOff));
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"02-mask-third-person.png"));
        Key(p,62);yield return new WaitForSecondsRealtime(.5f);
        Check(output,"thirdPersonFrontMaskHidden",p.Minecraft.CameraMode==2&&visor.All(r=>r.forceRenderingOff));
        Key(p,62);yield return new WaitForSecondsRealtime(.5f);
        Check(output,"returnToFirstPersonRestoresMask",visor.Any(r=>r.enabled&&!r.forceRenderingOff));
        var previous=Keyboard.current;var keyboard=InputSystem.AddDevice<Keyboard>("LethalCraft mask test");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(UnityEngine.InputSystem.Key.F7));yield return null;yield return null;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return new WaitForSecondsRealtime(.4f);
        Check(output,"f7DisablesFirstPersonMask",!p.appearance.ShowFirstPersonMask&&visor.All(r=>r.forceRenderingOff));
        Check(output,"maskSettingSaved",File.ReadAllText(Path.Combine(BepInEx.Paths.ConfigPath,"LethalCraft-tests.cfg")).Contains("ShowFirstPersonMask = false"));
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"03-mask-disabled.png"));
        p.SetNativeMode(true);yield return new WaitForSecondsRealtime(.3f);
        Check(output,"disabledMaskStaysOffInNativeMode",visor.All(r=>r.forceRenderingOff));
        p.appearance.ToggleMask();p.SetNativeMode(false);
        InputSystem.RemoveDevice(keyboard);previous?.MakeCurrent();p.controls.RefreshKeyboard();yield return Ready(p);

        var itemType=Resources.FindObjectsOfTypeAll<Item>().First(i=>i.itemName=="Pro-flashlight");
        var obj=UnityEngine.Object.Instantiate(itemType.spawnPrefab,player.gameplayCamera.transform.position+player.gameplayCamera.transform.forward,Quaternion.identity);
        var item=obj.GetComponent<GrabbableObject>();obj.GetComponent<Unity.Netcode.NetworkObject>().Spawn();
        yield return new WaitForSecondsRealtime(.4f);
        p.Yaw=0;p.Pitch=0;yield return new WaitForSecondsRealtime(.2f);
        item.transform.position=player.gameplayCamera.transform.position+player.gameplayCamera.transform.forward;
        item.fallTime=1f;GameNetworkManager.Instance.gameHasStarted=true;Physics.SyncTransforms();
        AccessTools.Method(typeof(GameNetcodeStuff.PlayerControllerB),"BeginGrabObject").Invoke(player,null);
        bool pickupHidden=false;float until=Time.realtimeSinceStartup+1.5f;
        while(Time.realtimeSinceStartup<until)
        {
            yield return new WaitForEndOfFrame();
            if(player.isGrabbingObjectAnimation&&item.GetComponentsInChildren<Renderer>(true).All(r=>r.forceRenderingOff))pickupHidden=true;
        }
        var renderers=item.GetComponentsInChildren<Renderer>(true);
        Check(output,"pickupAnimationHidesNativeItem",pickupHidden);
        Check(output,"heldNativeItemHidden",player.currentlyHeldObjectServer==item&&renderers.All(r=>r.forceRenderingOff));
        string ObjectPath(Transform t)=>t.parent==null?t.name:ObjectPath(t.parent)+"/"+t.name;
        File.WriteAllLines(Path.Combine(output,"nearby-renderers.txt"),UnityEngine.Object.FindObjectsOfType<Renderer>().Where(r=>(r.bounds.center-player.gameplayCamera.transform.position).sqrMagnitude<36).Select(r=>ObjectPath(r.transform)+" layer="+r.gameObject.layer+" enabled="+r.enabled+" off="+r.forceRenderingOff+" shadow="+r.shadowCastingMode+" bounds="+r.bounds));
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"04-native-item-hidden.png"));
        yield return new WaitForSecondsRealtime(.25f);
        p.SetNativeMode(true);yield return new WaitForSecondsRealtime(.5f);
        Check(output,"nativeModeRestoresHeldItem",renderers.Any(r=>r.enabled&&!r.forceRenderingOff));
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"05-native-item-restored.png"));
        yield return new WaitForSecondsRealtime(.25f);
        p.SetNativeMode(false);yield return Ready(p);
        player.DiscardHeldObject();yield return new WaitForSecondsRealtime(.7f);
        Check(output,"droppedItemVisibleAgain",renderers.Any(r=>r.enabled&&!r.forceRenderingOff));
        obj.GetComponent<Unity.Netcode.NetworkObject>().Despawn();
    }
}
