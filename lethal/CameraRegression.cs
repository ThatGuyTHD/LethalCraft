using System;
using System.Collections;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace LethalCraft;

// Isolated opt-in fixture. Exercises the actual Tab/Alt handlers and F5 bridge input.
internal static class CameraRegression
{
    static string output="";
    static int failures;
    static void Check(bool ok,string name)
    {if(!ok)failures++;File.AppendAllText(Path.Combine(output,"camera.txt"),(ok?"PASS ":"FAIL ")+name+"\n");}
    static IEnumerator Wait(Func<bool> predicate,string name,float seconds=25)
    {float until=Time.realtimeSinceStartup+seconds;while(!predicate()&&Time.realtimeSinceStartup<until)yield return new WaitForSecondsRealtime(.1f);Check(predicate(),name);}
    static IEnumerator Press(Keyboard keyboard,Key key)
    {
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(key));yield return null;yield return null;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return new WaitForSecondsRealtime(.3f);
    }
    static IEnumerator Capture(string name)
    {yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,name+".png"));}
    public static IEnumerator Run(Plugin p,string directory)
    {
        output=directory;failures=0;File.WriteAllText(Path.Combine(output,"camera.txt"),"");
        var player=Plugin.LocalPlayer!;var camera=player.gameplayCamera;
        var previous=Keyboard.current;var keyboard=InputSystem.AddDevice<Keyboard>("LethalCraft camera fixture");
        p.SetNativeMode(true);yield return new WaitForSecondsRealtime(.5f);
        Vector3 nativePosition=camera.transform.localPosition;
        float nativeHeight=player.thisController.height,nativeRadius=player.thisController.radius;
        p.SetNativeMode(false);yield return Wait(()=>p.Ready,"initial Minecraft ready");
        for(int round=0;round<2;round++)for(uint mode=0;mode<3;mode++)
        {
            for(int n=0;p.Minecraft.CameraMode!=mode&&n<3;n++)
            {p.Link.Input(1,62,1);p.Link.Input(1,62,0);yield return new WaitForSecondsRealtime(.5f);}
            string label="round "+round+" view "+mode;
            p.Yaw=63;p.Pitch=17;yield return new WaitForSecondsRealtime(.4f);
            Check(p.Minecraft.CameraMode==mode,label+" F5 selected");
            yield return Wait(()=>p.world.LocalAvatarVisible==(mode!=0),label+" Minecraft avatar visibility");
            yield return Press(keyboard,Key.Tab);
            Check(!p.Possessed,label+" Tab selects native control");
            Check(!p.world.LocalAvatarVisible,label+" native control hides local Minecraft body");
            Check(Vector3.Distance(camera.transform.localPosition,nativePosition)<.05f,label+" native camera position restored");
            Check(Math.Abs(Mathf.DeltaAngle(camera.transform.eulerAngles.y,63))<.1f,label+" native camera keeps facing direction");
            Check(Math.Abs(Mathf.DeltaAngle(camera.transform.eulerAngles.x,17))<.1f,label+" native camera keeps pitch");
            Check(Math.Abs(player.thisController.height-nativeHeight)<.001f&&Math.Abs(player.thisController.radius-nativeRadius)<.001f,label+" native capsule restored");
            if(round==0&&mode!=0)yield return Capture("native-from-view-"+mode);
            // Turn and move under native control while Minecraft keeps sending render frames.
            player.transform.rotation=Quaternion.Euler(0,84,0);
            AccessTools.Field(typeof(GameNetcodeStuff.PlayerControllerB),"cameraUp").SetValue(player,-9f);
            player.thisController.Move(player.transform.forward*.2f);
            yield return new WaitForSecondsRealtime(.5f);
            Check(!p.world.LocalAvatarVisible,label+" incoming frames do not restore hidden avatar");
            float yaw=camera.transform.eulerAngles.y,pitch=camera.transform.eulerAngles.x;
            yield return Press(keyboard,Key.Tab);yield return Wait(()=>p.Ready,label+" Tab returns to Minecraft");
            Check(p.Minecraft.CameraMode==mode,label+" F5 choice retained");
            Check(Math.Abs(Mathf.DeltaAngle(p.Yaw,yaw))<.1f&&Math.Abs(Mathf.DeltaAngle(p.Pitch,pitch))<.1f,label+" native look direction retained on return");
            yield return Wait(()=>p.world.LocalAvatarVisible==(mode!=0),label+" returned avatar visibility");
            Check(Vector3.Distance(p.world.LocalAvatarPosition,player.transform.position)<.15f,label+" returned avatar aligned with character");
            if(round==0&&mode!=0)yield return Capture("returned-view-"+mode);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.LeftAlt));yield return new WaitForSecondsRealtime(.4f);
            Check(!p.Possessed&&!p.world.LocalAvatarVisible,label+" Alt temporarily restores native view");
            Check(Math.Abs(Mathf.DeltaAngle(camera.transform.eulerAngles.y,p.Yaw))<.1f,label+" Alt camera faces native direction");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return Wait(()=>p.Ready,label+" Alt release returns to Minecraft");
        }
        InputSystem.RemoveDevice(keyboard);previous?.MakeCurrent();p.controls.RefreshKeyboard();
        File.AppendAllText(Path.Combine(output,"camera.txt"),"Failures: "+failures+"\n");
    }
}
