using System;
using HarmonyLib;
using Steamworks;
using UnityEngine;

namespace LethalCraft;

internal static class SteamHosting
{
    internal const string Unavailable="Steam is not connected. Quit Lethal Company, sign in to Steam, then use Play in the LethalCraft launcher. For offline play, choose LAN when the game starts.";

    internal static bool Connected
    {
        get
        {
            // Facepunch's IsLoggedOn getter throws if its native interface was never initialized.
            try{return SteamClient.IsValid&&SteamClient.IsLoggedOn;}catch(Exception){return false;}
        }
    }

    internal static void Install(Harmony patches)
    {
        patches.Patch(AccessTools.Method(typeof(GameNetworkManager),"StartHost"),prefix:new HarmonyMethod(typeof(SteamHosting),nameof(BeforeHost)));
        patches.Patch(AccessTools.Method(typeof(MenuManager),"ConfirmHostButton"),prefix:new HarmonyMethod(typeof(SteamHosting),nameof(BeforeHost)));
    }

    internal static bool BeforeHost()
    {
        if(GameNetworkManager.Instance==null||GameNetworkManager.Instance.disableSteam||Connected)return true;
        var menu=UnityEngine.Object.FindObjectOfType<MenuManager>();
        if(menu!=null)menu.DisplayMenuNotification(Unavailable,"Back");
        Plugin.Log.LogWarning("Online hosting blocked: Steam client is not initialized or not logged on.");
        return false;
    }
}
