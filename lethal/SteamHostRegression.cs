using System;
using System.Collections;
using System.IO;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LethalCraft;

// Explicit test flags only. Uses a separate save and a friends-only lobby; sends no invitations.
internal sealed class SteamHostRegression:MonoBehaviour
{
    IEnumerator Start()
    {
        var args=Environment.GetCommandLineArgs();
        bool unavailable=Array.IndexOf(args,"--lethalcraft-steam-unavailable-test")>=0;
        int outputIndex=Array.IndexOf(args,"--lethalcraft-test-output");
        string output=outputIndex>=0&&outputIndex+1<args.Length?args[outputIndex+1]:Path.Combine(BepInEx.Paths.PluginPath,"LethalCraft-steam-test");
        Directory.CreateDirectory(output);
        string report=Path.Combine(output,"result.txt");
        void Record(string message){File.AppendAllText(report,message+"\n");Plugin.Log.LogInfo("STEAM TEST: "+message);}
        File.WriteAllText(report,"");
        float deadline=Time.realtimeSinceStartup+100;
        PreInitSceneScript pre=null!;
        while(pre==null&&Time.realtimeSinceStartup<deadline){pre=FindObjectOfType<PreInitSceneScript>();yield return null;}
        if(pre==null){Record("FAIL launch options missing");Application.Quit();yield break;}
        yield return new WaitForSecondsRealtime(2);
        pre.ChooseLaunchOption(true);
        MenuManager menu=null!;
        while(menu==null&&Time.realtimeSinceStartup<deadline)
        {if(SceneManager.GetActiveScene().name=="MainMenu")menu=FindObjectOfType<MenuManager>();yield return null;}
        if(menu==null){Record("FAIL online menu missing");Application.Quit();yield break;}
        yield return new WaitForSecondsRealtime(1);
        Record("steamConnected="+SteamHosting.Connected);
        if(GameNetworkManager.Instance.disableSteam||SteamHosting.Connected==unavailable)
        {Record("FAIL unexpected Steam state for this fixture");Application.Quit();yield break;}
        menu.ClickHostButton();menu.HostSetLobbyPublic(false);
        menu.lobbyNameInputField.text="LethalCraft private hosting check";
        GameNetworkManager.Instance.currentSaveFileName="LethalCraft_SteamTest";
        menu.ConfirmHostButton();
        if(unavailable)
        {
            yield return null;
            bool shown=menu.menuNotification.activeSelf&&menu.menuNotificationText.text==SteamHosting.Unavailable;
            Record((shown&&!NetworkManager.Singleton.IsHost?"PASS":"FAIL")+" disconnected Steam produces a visible error and does not start a host");
        }
        else
        {
            deadline=Time.realtimeSinceStartup+120;
            while(Plugin.LocalPlayer==null&&Time.realtimeSinceStartup<deadline)yield return new WaitForSecondsRealtime(.2f);
            bool hosted=NetworkManager.Singleton.IsHost&&Plugin.LocalPlayer!=null&&GameNetworkManager.Instance.currentLobby.HasValue;
            if(GameNetworkManager.Instance.currentLobby.HasValue)GameNetworkManager.Instance.SetLobbyJoinable(false);
            Record((hosted?"PASS":"FAIL")+" Online > Host > Confirm creates a Steam lobby and loads the host into the ship");
        }
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"result.png"));
        yield return new WaitForSecondsRealtime(1);
        GameNetworkManager.Instance.LeaveCurrentSteamLobby();Application.Quit();
    }
}
