using System;
using System.Collections.Generic;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LethalCraft;

internal sealed class Controls : IDisposable
{
    readonly Plugin plugin;
    readonly Dictionary<Key,ushort> scans = new();
    readonly HashSet<ushort> held = new();
    readonly bool[] mouseHeld = new bool[3];
    readonly InputAction interact;
    Keyboard? textKeyboard;
    char highSurrogate;
    int lastTextFrame=-1;
    bool nativeEClaimed;
    readonly Dictionary<ushort,float> repeatAt=new();
    public int TextCharactersSent { get; private set; }
    public static bool DispatchingInteract;
    public Controls(Plugin plugin)
    {
        this.plugin=plugin;
        for(int i=0;i<26;i++)scans[(Key)Enum.Parse(typeof(Key),((char)('A'+i)).ToString())]=(ushort)(4+i);
        for(int i=1;i<=9;i++)scans[(Key)Enum.Parse(typeof(Key),"Digit"+i)]=(ushort)(29+i);
        scans[Key.Digit0]=39;scans[Key.Enter]=40;scans[Key.Escape]=41;scans[Key.Backspace]=42;scans[Key.Space]=44;
        scans[Key.Tab]=43;scans[Key.Minus]=45;scans[Key.Equals]=46;scans[Key.LeftBracket]=47;scans[Key.RightBracket]=48;
        scans[Key.Backslash]=49;scans[Key.Semicolon]=51;scans[Key.Quote]=52;scans[Key.Backquote]=53;
        scans[Key.Comma]=54;scans[Key.Period]=55;scans[Key.Slash]=56;
        scans[Key.F1]=58;scans[Key.F2]=59;scans[Key.F3]=60;scans[Key.F4]=61;scans[Key.F5]=62;scans[Key.F6]=63;
        scans[Key.RightArrow]=79;scans[Key.LeftArrow]=80;scans[Key.DownArrow]=81;scans[Key.UpArrow]=82;
        scans[Key.Home]=74;scans[Key.End]=77;scans[Key.Delete]=76;scans[Key.PageUp]=75;scans[Key.PageDown]=78;
        scans[Key.LeftCtrl]=224;scans[Key.LeftShift]=225;scans[Key.RightCtrl]=228;scans[Key.RightShift]=229;
        interact=new InputAction("LethalCraft interact",InputActionType.Button,"<Keyboard>/g");
        interact.performed+=ctx=>
        {
            if(!plugin.Possessed||plugin.MinecraftMenu||Plugin.LocalPlayer==null)return;
            DispatchingInteract=true;
            try{AccessTools.Method(typeof(PlayerControllerB),"Interact_performed").Invoke(Plugin.LocalPlayer,new object[]{ctx});}
            finally{DispatchingInteract=false;}
        };
        interact.Enable();InputSystem.onDeviceChange+=DeviceChanged;RefreshKeyboard();
    }
    void DeviceChanged(InputDevice device,InputDeviceChange change)=>RefreshKeyboard();
    internal void RefreshKeyboard()
    {
        var current=Keyboard.current;
        if(current==textKeyboard)return;
        if(textKeyboard!=null)textKeyboard.onTextInput-=Text;
        textKeyboard=current;
        if(textKeyboard!=null)textKeyboard.onTextInput+=Text;
    }
    void Text(char character)
    {
        if(!plugin.MinecraftMenu||(!Application.isFocused&&!SmokeTest.Active))return;
        lastTextFrame=Time.frameCount;SendCharacter(character);
    }
    void SendCharacter(char character)
    {
        if(char.IsControl(character))return;
        if(char.IsHighSurrogate(character)){highSurrogate=character;return;}
        int codepoint=character;
        if(char.IsLowSurrogate(character))
        {
            if(highSurrogate==0)return;
            codepoint=char.ConvertToUtf32(highSurrogate,character);
        }
        highSurrogate='\0';
        if(plugin.Link.Input(5,0,codepoint))TextCharactersSent++;
    }
    // IMGUI receives OS text even when a keyboard was created after the plugin's Awake.
    internal void GuiText(Event e)
    {
        if(plugin.MinecraftMenu&&Application.isFocused&&e.type==EventType.KeyDown
            &&e.character!=0&&lastTextFrame!=Time.frameCount)SendCharacter(e.character);
    }
    internal static bool HasNativeTarget(PlayerControllerB p)
    {
        if(p.hoveringOverTrigger!=null&&p.hoveringOverTrigger.gameObject.activeInHierarchy)return true;
        int mask=(int)AccessTools.Field(typeof(PlayerControllerB),"interactableObjectsMask").GetValue(p);
        if(!Physics.Raycast(p.gameplayCamera.transform.position,p.gameplayCamera.transform.forward,out var hit,p.grabDistance,mask))return false;
        return hit.collider.GetComponent<GrabbableObject>()!=null||hit.collider.GetComponent<InteractTrigger>()!=null;
    }
    internal bool NativeInteract(PlayerControllerB p)
    {
        if(plugin.MinecraftMenu||!HasNativeTarget(p))return false;
        nativeEClaimed=true;return true;
    }
    public void Poll()
    {
        RefreshKeyboard();var k=Keyboard.current;var mouse=Mouse.current;
        if(!Application.isFocused){Release();return;}
        if(k!=null)
        {
            if(k.oKey.wasPressedThisFrame&&!plugin.MinecraftMenu)plugin.Link.Input(8);
            if(!k.eKey.isPressed)nativeEClaimed=false;
            foreach(var pair in scans)
            {
                bool menu=plugin.MinecraftMenu;
                if(!menu&&(pair.Key==Key.G||pair.Key==Key.O||pair.Key==Key.Tab))continue;
                if(pair.Key==Key.Escape&&!plugin.MinecraftMenu)continue;
                bool down=k[pair.Key].isPressed;
                if(!menu&&(pair.Key==Key.E||pair.Key==Key.I))
                {
                    if(k[pair.Key].wasPressedThisFrame&&(pair.Key==Key.I||(!nativeEClaimed&&!HasNativeTarget(Plugin.LocalPlayer!))))
                        plugin.Link.Input((ushort)(pair.Key==Key.I?10:9));
                    continue;
                }
                if(pair.Key==Key.E&&nativeEClaimed)continue;
                if(down&&!held.Contains(pair.Value))
                {
                    if(plugin.Link.Input(1,pair.Value,1)){held.Add(pair.Value);repeatAt[pair.Value]=Time.unscaledTime+.45f;}
                }
                else if(!down&&held.Contains(pair.Value)){if(plugin.Link.Input(1,pair.Value,0)){held.Remove(pair.Value);repeatAt.Remove(pair.Value);}}
                else if(down&&menu&&(pair.Value==42||pair.Value>=74&&pair.Value<=82)&&repeatAt.TryGetValue(pair.Value,out float next)&&Time.unscaledTime>=next)
                {plugin.Link.Input(1,pair.Value,1);repeatAt[pair.Value]=Time.unscaledTime+.04f;}
            }
        }
        if(mouse==null)return;
        if(plugin.MinecraftMenu)
        {
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            Vector2 pos=mouse.position.ReadValue();float scale=Math.Min(Screen.width,1920)/(float)Math.Max(1,Screen.width);
            plugin.Link.Input(4,0,Mathf.RoundToInt(pos.x*scale),Mathf.RoundToInt((Screen.height-pos.y)*scale));
        }
        else
        {
            Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;
            Vector2 delta=mouse.delta.ReadValue();float f=plugin.Minecraft.Sensitivity*.6f+.2f;float sens=f*f*f*8f*.15f;
            plugin.Yaw+=delta.x*sens;plugin.Pitch=Mathf.Clamp(plugin.Pitch-delta.y*sens,-89,89);
        }
        bool[] buttons={mouse.leftButton.isPressed,mouse.middleButton.isPressed,mouse.rightButton.isPressed};
        for(int i=0;i<3;i++)if(buttons[i]!=mouseHeld[i]){if(plugin.Link.Input(2,(ushort)(i+1),buttons[i]?1:0))mouseHeld[i]=buttons[i];}
        float wheel=mouse.scroll.ReadValue().y;if(wheel!=0)plugin.Link.Input(3,0,Mathf.RoundToInt(wheel));
    }
    public void Release(){held.Clear();repeatAt.Clear();highSurrogate='\0';Array.Clear(mouseHeld,0,mouseHeld.Length);plugin.Link?.Input(6);}
    public void Dispose(){Release();interact.Dispose();InputSystem.onDeviceChange-=DeviceChanged;if(textKeyboard!=null)textKeyboard.onTextInput-=Text;}
}
