using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace LethalCraft;

internal sealed class Appearance : IDisposable
{
    readonly ConfigEntry<bool> firstPersonMask;
    readonly HiddenRenderers mask = new(), held = new();
    readonly List<Transform> maskRoots = new();
    readonly List<Transform> itemRoots = new();
    static readonly System.Reflection.FieldInfo grabbing = AccessTools.Field(typeof(PlayerControllerB), "currentlyGrabbingObject");
    public bool ShowFirstPersonMask => firstPersonMask.Value;
    public int MaskRendererCount => mask.Count;
    public int HeldRendererCount => held.Count;
    public Appearance(ConfigFile config) => firstPersonMask = config.Bind("Appearance", "ShowFirstPersonMask", true,
        "Show the Lethal Company helmet mask in first person. F7 toggles and saves this. Always hidden in Minecraft third person.");
    public void ToggleMask() => firstPersonMask.Value = !firstPersonMask.Value;

    public void Apply(PlayerControllerB? player, bool minecraftView, uint cameraMode)
    {
        maskRoots.Clear(); itemRoots.Clear();
        if (player != null)
        {
            if (!ShowFirstPersonMask || (minecraftView && cameraMode != 0))
            {
                if (player.localVisor != null) maskRoots.Add(player.localVisor);
                var hud = HUDManager.Instance;
                if (hud != null)
                {
                    if (hud.visorCracksObject != null) maskRoots.Add(hud.visorCracksObject.transform);
                    if (hud.helmetGoop != null) maskRoots.Add(hud.helmetGoop.transform);
                    if (hud.gasHelmetAnimator != null) maskRoots.Add(hud.gasHelmetAnimator.transform);
                }
            }
            if (minecraftView)
            {
                if (player.currentlyHeldObjectServer != null) itemRoots.Add(player.currentlyHeldObjectServer.transform);
                if (player.isGrabbingObjectAnimation && grabbing.GetValue(player) is GrabbableObject item && item != null)
                    itemRoots.Add(item.transform);
            }
        }
        mask.Set(maskRoots); held.Set(itemRoots);
    }
    public void Dispose() { mask.Restore(); held.Restore(); }

    internal sealed class HiddenRenderers
    {
        readonly List<Transform> roots = new();
        readonly Dictionary<Renderer, bool> saved = new();
        public int Count => saved.Count;
        public void Set(List<Transform> requested)
        {
            bool changed = roots.Count != requested.Count;
            if (!changed) for (int i = 0; i < roots.Count; i++) if (roots[i] != requested[i]) { changed = true; break; }
            if (changed)
            {
                Restore(); roots.AddRange(requested);
                foreach (var root in roots)
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                        if (!saved.ContainsKey(renderer)) saved.Add(renderer, renderer.forceRenderingOff);
            }
            foreach (var pair in saved) if (pair.Key != null) pair.Key.forceRenderingOff = true;
        }
        public void Restore()
        {
            foreach (var pair in saved) if (pair.Key != null) pair.Key.forceRenderingOff = pair.Value;
            saved.Clear(); roots.Clear();
        }
    }
}
