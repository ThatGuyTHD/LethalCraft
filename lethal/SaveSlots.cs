using System;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using GameNetcodeStuff;

namespace LethalCraft;

// Identity lives IN the native save, so deleting/recreating a slot cannot revive old builds.
internal sealed class SaveSlots
{
    internal const string IdentityKey = "LethalCraftWorldId";
    readonly ConfigEntry<string> legacySlot, legacyIdentity;
    StartOfRound? boundRound;
    string boundFile = "";
    public string WorldName { get; private set; } = "";
    public string Error { get; private set; } = "";
    public uint Revision { get; private set; } = 1;
    public uint SaveRequest { get; private set; }
    public uint Flags { get; private set; }

    public SaveSlots(ConfigFile config, bool testing)
    {
        int selected = ES3.Load<int>("SelectedFile", "LCGeneralSaveData", 0);
        string owner = selected == -1 ? "LCChallengeFile" : "LCSaveFile" + (Math.Max(0, Math.Min(selected, 2)) + 1);
        legacySlot = config.Bind("Saves", "LegacySaveSlot", testing ? "LethalCraft_SlotTest_A" : owner,
            "The old shared Minecraft world is copied once to this native slot. Other slots start fresh.");
        legacyIdentity = config.Bind("Saves", "LegacyWorldId", "",
            "Migration record. Retain this value so deleting/resetting a slot does not import old progress again.");
    }

    public bool Tick(StartOfRound? round, PlayerControllerB? player, string guestWorld="")
    {
        var manager = GameNetworkManager.Instance;
        bool active = round != null && player != null && player.IsSpawned && manager != null;
        string file = active ? (manager!.isHostingGame?manager.currentSaveFileName:"guest:"+guestWorld) : "";
        if (file == boundFile && round == boundRound) return false;
        boundRound = round; boundFile = file;
        WorldName = ""; Flags = 0; Error = ""; Revision++;
        if (!active) return true;
        try
        {
            // Guests connect to the host's world; no private fallback that could lose shared progress.
            if (!manager!.isHostingGame)
            {
                WorldName = guestWorld;
                return true;
            }
            if (!Regex.IsMatch(file, "^[A-Za-z0-9_-]{1,40}$")) throw new InvalidOperationException("Unsupported save filename");
            string identity = ES3.Load<string>(IdentityKey, file, "");
            if (identity.Length == 0)
            {
                identity = Guid.NewGuid().ToString("N");
                ES3.Save(IdentityKey, identity, file);
            }
            if (!Regex.IsMatch(identity, "^[a-f0-9]{32}$")) throw new InvalidOperationException("Invalid Minecraft world identity in " + file);
            if (file == legacySlot.Value && legacyIdentity.Value.Length == 0) legacyIdentity.Value = identity;
            Flags = identity == legacyIdentity.Value ? 1u : 0u;
            WorldName = "LethalCraft_" + file + "_" + identity;
            Plugin.Log.LogInfo("Minecraft save: " + file + " -> " + WorldName);
        }
        catch (Exception e)
        {
            Error = "Cannot open Minecraft save: " + e.Message;
            Plugin.Log.LogError(Error);
        }
        return true;
    }

    public void RequestSave() { SaveRequest++; }

    public void ResetCampaign(GameNetworkManager manager)
    {
        if (!manager.isHostingGame) return;
        string file = manager.currentSaveFileName;
        // Only called after the game's own campaign reset. Previous world folders stay archived.
        ES3.Save(IdentityKey, Guid.NewGuid().ToString("N"), file);
        boundFile = "";
    }
}
