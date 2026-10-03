using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using LethalCraft.Bridge;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LethalCraft;

[BepInPlugin("local.lethalcraft.bridge", "LethalCraft", "0.2.1")]
[BepInProcess("Lethal Company.exe")]
[DefaultExecutionOrder(-10000)]
public sealed class Plugin : BaseUnityPlugin
{
    public static Plugin Instance = null!;
    public static ManualLogSource Log = null!;
    public static PlayerControllerB? LocalPlayer => GameNetworkManager.Instance?.localPlayerController;
    internal SharedLink Link = null!;
    internal PlayerState Minecraft;
    internal WorldCollision collisions = null!;
    internal WorldRenderer world = null!;
    internal Controls controls = null!;
    internal SaveSlots saves = null!;
    internal Appearance appearance = null!;
    internal Network.Multiplayer multiplayer = null!;
    int collisionPlayer;
    bool minecraftView;
    float maskNoticeUntil;
    CombatBridge combat = null!;
    Harmony patches = null!;
    PlayerControllerB? bound;
    bool enabledByUser = true, nativeMode, suspended = true, previouslyPossessed, failed, diagnostics;
    bool savedBackground, savedArms;
    float savedHeight, savedRadius, savedFov, savedStep;
    Vector3 savedCentre, savedCameraPosition;
    readonly Dictionary<Renderer,bool> hiddenRenderers=new();
    readonly Dictionary<Collider,bool> extraColliders=new();
    internal float WeaponDamageMultiplier;
    uint teleport = 1, epoch = 1;
    int levelKey = int.MinValue;
    public bool Possessed { get; private set; }
    public bool Ready => Possessed && Minecraft.TeleportAck == teleport;
    internal bool SessionReady => saves.WorldName.Length > 0 && Minecraft.SessionAck == saves.Revision;
    public bool MinecraftMenu => Possessed && Minecraft.MenuOpen;
    internal float Yaw, Pitch;
    public string Status { get; private set; } = "Starting bridge";

    void Awake()
    {
        Instance=this;Log=Logger;savedBackground=Application.runInBackground;Application.runInBackground=true;
        try
        {
            bool testing=Array.IndexOf(Environment.GetCommandLineArgs(),"--lethalcraft-smoke")>=0;
            bool normalTestLink=Array.IndexOf(Environment.GetCommandLineArgs(),"--lethalcraft-smoke-normal-link")>=0;
            bool testGuest=testing&&Array.IndexOf(Environment.GetCommandLineArgs(),"--lethalcraft-multiplayer-guest")>=0;
            Link=new SharedLink(testing&&!normalTestLink?Protocol.MappingName+(testGuest?"_guest":"_smoke"):Protocol.MappingName);collisions=new WorldCollision(Link);collisions.Reset(epoch);
            WeaponDamageMultiplier=Config.Bind("Combat","WeaponDamageMultiplier",2f,"Minecraft weapon damage multiplier against killable Lethal Company enemies (1 = original difficulty).").Value;
            WeaponDamageMultiplier=Mathf.Clamp(WeaponDamageMultiplier,.1f,20f);
            world=new WorldRenderer();controls=new Controls(this);combat=new CombatBridge(this);
            var settings=testing?new BepInEx.Configuration.ConfigFile(System.IO.Path.Combine(BepInEx.Paths.ConfigPath,"LethalCraft-tests.cfg"),true):Config;
            saves=new SaveSlots(settings,testing);appearance=new Appearance(settings);
            multiplayer=new Network.Multiplayer(this);
            patches=new Harmony("local.lethalcraft.bridge");
            patches.Patch(AccessTools.Method(typeof(GameNetworkManager),"SaveGame"),postfix:new HarmonyMethod(typeof(Patches),nameof(Patches.Saved)));
            patches.Patch(AccessTools.Method(typeof(GameNetworkManager),"ResetSavedGameValues"),postfix:new HarmonyMethod(typeof(Patches),nameof(Patches.ResetSaved)));
            patches.Patch(AccessTools.Method(typeof(PlayerControllerB),"Update"),transpiler:new HarmonyMethod(typeof(Patches),nameof(Patches.Movement)));
            patches.Patch(AccessTools.Method(typeof(PlayerControllerB),"LateUpdate"),postfix:new HarmonyMethod(typeof(Patches),nameof(Patches.AfterPlayer)));
            patches.Patch(AccessTools.Method(typeof(PlayerControllerB),"TeleportPlayer"),postfix:new HarmonyMethod(typeof(Patches),nameof(Patches.Teleported)));
            foreach(string name in new[]{"Jump_performed","Crouch_performed","ScrollMouse_performed","ActivateItem_performed","ActivateItem_canceled",
                "ItemSecondaryUse_performed","ItemTertiaryUse_performed","Discard_performed","InspectItem_performed","PlayerLookInput","Look_performed"})
            {
                MethodInfo m=AccessTools.Method(typeof(PlayerControllerB),name);
                if(m!=null)patches.Patch(m,prefix:new HarmonyMethod(typeof(Patches),nameof(Patches.NativeInput)));
            }
            patches.Patch(AccessTools.Method(typeof(PlayerControllerB),"Interact_performed"),prefix:new HarmonyMethod(typeof(Patches),nameof(Patches.Interact)));
            patches.Patch(AccessTools.Method(typeof(PlayerControllerB),"ClickHoldInteraction"),prefix:new HarmonyMethod(typeof(Patches),nameof(Patches.HoldInteract)));
            patches.Patch(AccessTools.Method(typeof(PlayerControllerB),"OpenMenu_performed"),prefix:new HarmonyMethod(typeof(Patches),nameof(Patches.OpenMenu)));
            patches.Patch(AccessTools.Method(typeof(HUDManager),"EnableChat_performed"),prefix:new HarmonyMethod(typeof(Patches),nameof(Patches.NativeChat)));
            patches.Patch(AccessTools.Method(typeof(PlayerControllerB),"DamagePlayer"),prefix:new HarmonyMethod(typeof(Patches),nameof(Patches.Damage)));
            patches.Patch(AccessTools.Method(typeof(PlayerControllerB),"KillPlayer"),prefix:new HarmonyMethod(typeof(Patches),nameof(Patches.Kill)));
            patches.Patch(AccessTools.Method(typeof(Landmine),"SpawnExplosion"),prefix:new HarmonyMethod(typeof(Patches),nameof(Patches.Explosion)),finalizer:new HarmonyMethod(typeof(Patches),nameof(Patches.ExplosionDone)));
            foreach(var method in new[]{AccessTools.Method(typeof(EnemyAICollisionDetect),"OnTriggerStay"),AccessTools.Method(typeof(Turret),"Update"),AccessTools.Method(typeof(ShotgunItem),"ShootGun")})
                if(method!=null)patches.Patch(method,prefix:new HarmonyMethod(typeof(Patches),nameof(Patches.Attack)),finalizer:new HarmonyMethod(typeof(Patches),nameof(Patches.AttackDone)));
            Logger.LogInfo("LethalCraft 0.2.1 loaded. Steam/LAN multiplayer enabled. E: interact; I: inventory; F7: mask; Tab/Alt: native controls.");
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"--lethalcraft-smoke")>=0)gameObject.AddComponent<SmokeTest>();
        }
        catch(Exception e){failed=true;Logger.LogError(e);Cleanup();}
    }

    void Update()
    {
        if(failed)return;
        try
        {
            var keyboard=Keyboard.current;
            if(keyboard?.f8Key.wasPressedThisFrame==true){enabledByUser=!enabledByUser;Reanchor();}
            if(keyboard?.f9Key.wasPressedThisFrame==true)diagnostics=!diagnostics;
            if(keyboard?.f7Key.wasPressedThisFrame==true){appearance.ToggleMask();maskNoticeUntil=Time.unscaledTime+3;}
            if(keyboard?.tabKey.wasPressedThisFrame==true&&!MinecraftMenu&&LocalPlayer!=null
                &&!LocalPlayer.inTerminalMenu&&!LocalPlayer.isTypingChat&&!LocalPlayer.quickMenuManager.isMenuOpen)SetNativeMode(!nativeMode);
            Link.Heartbeat();bool valid=Link.TryPlayer(out PlayerState state);if(valid)Minecraft=state;
            controls.RefreshKeyboard();
            PlayerControllerB? p=LocalPlayer;
            if(p!=bound){RestorePlayer();bound=p;if(p!=null)BindPlayer(p);Reanchor();}
            bool playable=p!=null&&p.isPlayerControlled&&!p.isPlayerDead;
            var round=StartOfRound.Instance;
            multiplayer.Tick();
            if(saves.Tick(round,p,multiplayer.GuestWorld)){epoch++;collisions.Reset(epoch);world.Clear();controls.Release();Reanchor();}
            bool loading=round!=null&&(round.newGameIsLoading||round.beganLoadingNewLevel||(!round.inShipPhase&&!round.shipHasLanded)||round.travellingToNewLevel||round.shipIsLeaving||round.suckingPlayersOutOfShip);
            int newKey=round==null?0:unchecked(round.currentLevelID*397^round.randomMapSeed);
            if(newKey!=levelKey){levelKey=newKey;epoch++;collisions.Reset(epoch);Reanchor();}
            bool native= !playable||loading||nativeMode||keyboard?.leftAltKey.isPressed==true
                ||(p!=null&&(p.inTerminalMenu||p.isTypingChat||p.inSpecialInteractAnimation||p.isClimbingLadder||p.inAnimationWithEnemy!=null||p.quickMenuManager.isMenuOpen));
            if(native!=suspended){suspended=native;Reanchor();controls.Release();}
            Possessed=enabledByUser&&valid&&Minecraft.InWorld&&SessionReady&&!native;
            multiplayer.PublishMode(Possessed);
            minecraftView=enabledByUser&&valid&&Minecraft.InWorld&&SessionReady&&playable&&!loading&&!nativeMode
                &&keyboard?.leftAltKey.isPressed!=true&&p!=null&&!p.inTerminalMenu&&!p.isTypingChat&&!p.quickMenuManager.isMenuOpen;
            if(Possessed!=previouslyPossessed)
            {
                previouslyPossessed=Possessed;controls.Release();
                if(Possessed){Reanchor();ApplyPlayerSize();}else RestorePlayer();
            }
            if(p!=null&&playable)
            {
                if(!Possessed){Yaw=p.gameplayCamera.transform.eulerAngles.y;Pitch=Signed(p.gameplayCamera.transform.eulerAngles.x);}
                else if(!SmokeTest.Active) controls.Poll();
                Vector3 collisionCentre=p.transform.position;
                if(multiplayer.Role==1&&multiplayer.ConnectedPeers>0&&round!=null)
                {
                    var players=round.allPlayerScripts;
                    var candidate=players[collisionPlayer++%players.Length];
                    if(candidate!=null&&candidate.isPlayerControlled&&!candidate.isPlayerDead)collisionCentre=candidate.transform.position;
                }
                collisions.Tick(collisionCentre);
                combat.Tick(p);
            }
            Vector3 feet=p!=null?p.transform.position:Vector3.zero;
            Vector3 mc=Coordinates.ToMinecraft(feet);
            uint flags=playable?1u:0u;
            if(native||!enabledByUser||!SessionReady)flags|=2;
            if(loading||!playable)flags|=4;
            int w=Math.Min(Screen.width,1920),h=Math.Max(1,Mathf.RoundToInt(w*(float)Screen.height/Math.Max(1,Screen.width)));
            Link.PublishHost(flags,unchecked((uint)levelKey),epoch,mc.x,mc.y,mc.z,Yaw,Pitch,teleport,w,h,
                session:saves.Revision,saveRequest:saves.SaveRequest,saveFlags:saves.Flags,worldName:saves.WorldName,
                networkRole:multiplayer.Role,connectPort:(uint)multiplayer.LocalPort);
            if(Link.Connected)
            {
                Link.ReadRender(world.Receive,512);world.Flush();world.UpdateOverlay(Link);
                Link.ReadEvents(e=>{if(SessionReady&&(Possessed||e.Type==1||e.Type==3))combat.Event(e);});
                world.PositionAvatar(Minecraft);
            }
            world.Visible(enabledByUser&&valid&&Minecraft.InWorld&&SessionReady&&playable);
            Status=saves.Error.Length>0?saves.Error:Minecraft.SessionError!=0?"Minecraft connection/save failed; see Minecraft log":!enabledByUser?"Bridge off · F8 to enable":!Link.Connected?"Waiting for Minecraft":!SessionReady||!Minecraft.InWorld?(multiplayer.Role==2?multiplayer.Status:"Opening Minecraft save slot"):
                native?"Lethal Company controls · Tab to switch":!Ready?"Loading collision": "Minecraft controls · E interact · I inventory · Alt: Lethal Company";
            Link.Write32(0x80,(Possessed?1u:0u)|(Ready?2u:0u)|(valid?4u:0u)|(native?8u:0u));
        }
        catch(Exception e)
        {
            Logger.LogError(e);Status="Bridge stopped: "+e.Message;failed=true;Possessed=false;RestorePlayer();
            controls?.Release();world?.Visible(false);
        }
    }
    void BindPlayer(PlayerControllerB p)
    {
        var cc=p.thisController;savedHeight=cc.height;savedRadius=cc.radius;savedCentre=cc.center;savedStep=cc.stepOffset;
        savedFov=p.gameplayCamera.fieldOfView;savedCameraPosition=p.gameplayCamera.transform.localPosition;
        savedArms=p.thisPlayerModelArms.enabled;Yaw=p.gameplayCamera.transform.eulerAngles.y;Pitch=Signed(p.gameplayCamera.transform.eulerAngles.x);
    }
    void ApplyPlayerSize()
    {
        if(bound==null)return;var cc=bound.thisController;cc.height=1.8f;cc.radius=.3f;cc.center=new Vector3(0,.9f,0);cc.stepOffset=.5f;
        foreach(var r in bound.GetComponentsInChildren<Renderer>(true))
        {
            if(r.GetComponentInParent<GrabbableObject>()!=null)continue;
            hiddenRenderers[r]=r.forceRenderingOff;r.forceRenderingOff=true;
        }
        foreach(var c in bound.GetComponentsInChildren<Collider>(true))
        {
            if(c==cc||c.GetComponentInParent<GrabbableObject>()!=null)continue;
            extraColliders[c]=c.enabled;c.enabled=false;
        }
    }
    void RestorePlayer()
    {
        if(bound==null)return;var cc=bound.thisController;cc.height=savedHeight;cc.radius=savedRadius;cc.center=savedCentre;cc.stepOffset=savedStep;
        bound.gameplayCamera.fieldOfView=savedFov;bound.gameplayCamera.transform.localPosition=savedCameraPosition;
        bound.thisPlayerModelArms.enabled=savedArms;
        foreach(var pair in hiddenRenderers)if(pair.Key!=null)pair.Key.forceRenderingOff=pair.Value;
        hiddenRenderers.Clear();
        foreach(var pair in extraColliders)if(pair.Key!=null)pair.Key.enabled=pair.Value;
        extraColliders.Clear();
    }
    internal void Reanchor(){teleport++;}
    internal void SetNativeMode(bool native){nativeMode=native;Reanchor();}
    static float Signed(float angle)=>angle>180?angle-360:angle;
    internal CollisionFlags Move(CharacterController controller,Vector3 original)
    {
        if(!Possessed||bound==null||controller!=bound.thisController)return controller.Move(original);
        if(!Ready||MinecraftMenu)return CollisionFlags.None;
        Vector3 target=Coordinates.ToUnity(Minecraft.X,Minecraft.Y,Minecraft.Z);
        Vector3 delta=target-controller.transform.position;
        if(delta.sqrMagnitude>400){Reanchor();return CollisionFlags.None;}
        // Minecraft resolves movement against both worlds. The native capsule is only the
        // aligned target for enemies/triggers; a second Move() would clip the two avatars apart.
        controller.transform.position=target;
        return (Minecraft.Flags&4)!=0?CollisionFlags.Below:CollisionFlags.None;
    }
    internal void CameraAfter(PlayerControllerB p)
    {
        multiplayer.ApplyRemoteAppearance();
        if(p==bound)appearance.Apply(p,minecraftView,Minecraft.CameraMode);
        if(!Possessed||p!=bound)return;
        p.ResetFallGravity(); // Minecraft owns gravity; do not accumulate a second native fall timer.
        var cc=p.thisController;float height=(Minecraft.Flags&8)!=0?1.5f:1.8f;cc.height=height;cc.center=new Vector3(0,height*.5f,0);
        p.transform.rotation=Quaternion.Euler(0,Yaw,0);
        AccessTools.Field(typeof(PlayerControllerB),"cameraUp").SetValue(p,Pitch);
        Camera camera=p.gameplayCamera;Vector3 eye=p.transform.position+Vector3.up*Mathf.Clamp(Minecraft.EyeHeight,.2f,2f);
        Quaternion rotation=Quaternion.Euler(Pitch,Yaw,0);
        if(Minecraft.CameraMode!=0)
        {
            bool front=Minecraft.CameraMode==2;float distance=Mathf.Clamp(Minecraft.CameraDistance,0,6);
            eye+=rotation*Vector3.forward*(front?distance:-distance);
            if(front)rotation=Quaternion.Euler(-Pitch,Yaw+180,0);
        }
        camera.transform.SetPositionAndRotation(eye,rotation);camera.fieldOfView=Mathf.Clamp(Minecraft.Fov,30,120);
        p.thisPlayerModelArms.enabled=false;p.sprintMeter=1;
        foreach(var pair in hiddenRenderers)if(pair.Key!=null)pair.Key.forceRenderingOff=true;
        foreach(var pair in extraColliders)if(pair.Key!=null)pair.Key.enabled=false;
    }
    void OnGUI()
    {
        if(!failed)controls?.GuiText(Event.current);
        if(Possessed)world?.DrawOverlay();
        if(Time.unscaledTime<maskNoticeUntil)GUI.Label(new Rect(12,60,650,25),appearance.ShowFirstPersonMask?"Helmet mask: first person only · F7 to hide":"Helmet mask: hidden · F7 to show in first person");
        if(!Possessed||diagnostics)
        {
            GUI.color=Color.white;GUI.Label(new Rect(12,10,850,25),"LethalCraft · "+Status);
            if(diagnostics)GUI.Label(new Rect(12,34,950,25),$"Regions {collisions?.RegionsSent}, unsupported meshes {collisions?.UnsupportedMeshes}, sections {world?.SectionCount}, frame {Minecraft.Frame}, health {Minecraft.Health:0.0}");
        }
    }
    void Cleanup()
    {
        Possessed=false;RestorePlayer();appearance?.Dispose();multiplayer?.Dispose();controls?.Dispose();combat?.Dispose();collisions?.Dispose();world?.Dispose();Link?.Dispose();patches?.UnpatchSelf();
        Application.runInBackground=savedBackground;
    }
    void OnDestroy(){if(Instance==this)Cleanup();}
    internal void FlashEnemy(uint actor)=>combat.Flash(actor);
    public static class Patches
    {
        public static void Saved()=>Instance.saves?.RequestSave();
        public static void ResetSaved(GameNetworkManager __instance)=>Instance.saves?.ResetCampaign(__instance);
        public static IEnumerable<CodeInstruction> Movement(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original=AccessTools.Method(typeof(CharacterController),nameof(CharacterController.Move));
            MethodInfo replacement=AccessTools.Method(typeof(Patches),nameof(Move));int count=0;
            foreach(CodeInstruction instruction in instructions)
            {
                if(instruction.Calls(original)){instruction.opcode=OpCodes.Call;instruction.operand=replacement;count++;}yield return instruction;
            }
            if(count==0)throw new InvalidOperationException("Lethal Company movement changed; bridge cannot safely take control.");
        }
        public static CollisionFlags Move(CharacterController controller,Vector3 movement)=>Instance.Move(controller,movement);
        public static void AfterPlayer(PlayerControllerB __instance)=>Instance.CameraAfter(__instance);
        public static void Teleported(PlayerControllerB __instance){if(__instance==LocalPlayer)Instance.Reanchor();}
        public static bool NativeInput(PlayerControllerB __instance)=>__instance!=LocalPlayer||!Instance.Possessed;
        public static bool Interact(PlayerControllerB __instance)=>NativeInput(__instance)||Controls.DispatchingInteract||Instance.controls.NativeInteract(__instance);
        public static bool HoldInteract(PlayerControllerB __instance)=>NativeInput(__instance)||!Instance.MinecraftMenu;
        public static bool OpenMenu(PlayerControllerB __instance)=>__instance!=LocalPlayer||!Instance.MinecraftMenu;
        public static bool NativeChat()=>!Instance.Possessed;
        public static bool Damage(PlayerControllerB __instance,int damageNumber,CauseOfDeath causeOfDeath,bool fallDamage,Vector3 force)
            =>Instance.combat==null||Instance.combat.Damage(__instance,damageNumber,causeOfDeath,fallDamage,force);
        public static bool Kill(PlayerControllerB __instance,CauseOfDeath causeOfDeath,Vector3 bodyVelocity)
            =>Instance.combat==null||Instance.combat.NativeDeath(__instance,causeOfDeath,bodyVelocity);
        public static void Explosion(Vector3 explosionPosition,float killRange,float damageRange,int nonLethalDamage,bool goThroughCar,out bool __state)
        {
            __state=Instance.combat.NativeBlastHandled;
            Instance.combat.NativeBlastHandled=false;
            Instance.combat.NativeExplosion(explosionPosition,killRange,damageRange,nonLethalDamage,goThroughCar);
        }
        public static void ExplosionDone(bool __state){Instance.combat.NativeBlastHandled=__state;}
        public static void Attack(Component __instance,out Vector3? __state)
        {
            __state=Instance.combat.AttackOrigin;
            Instance.combat.AttackOrigin=__instance is EnemyAICollisionDetect collision&&collision.mainScript!=null?collision.mainScript.transform.position:__instance.transform.position;
        }
        public static void AttackDone(Vector3? __state){Instance.combat.AttackOrigin=__state;}
    }
}
