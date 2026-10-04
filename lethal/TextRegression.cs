using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LethalCraft;

// Explicit smoke fixture: send both Unity text callbacks, including reversed order,
// adjacent frames, a replaced keyboard and legitimate repeated/Unicode characters.
internal static class TextRegression
{
    static string output="";
    static int failures;
    static void Check(bool ok,string name)
    {if(!ok)failures++;File.AppendAllText(Path.Combine(output,"typing.txt"),(ok?"PASS ":"FAIL ")+name+"\n");}
    static void Key(Plugin p,ushort code){p.Link.Input(1,code,1);p.Link.Input(1,code,0);}
    static IEnumerator Wait(Func<bool> predicate,string name)
    {float until=Time.realtimeSinceStartup+10;while(!predicate()&&Time.realtimeSinceStartup<until)yield return new WaitForSecondsRealtime(.1f);Check(predicate(),name);}
    static IEnumerator Both(Plugin p,Keyboard keyboard,string text,bool guiFirst)
    {
        for(int i=0;i<text.Length;i++)
        {
            var input=new Event{type=EventType.KeyDown,character=text[i]};
            if(guiFirst)p.controls.GuiText(input);else keyboard.OnTextInput(text[i]);
            if(i%2==0)yield return null; // Other half arrive in the same frame.
            if(guiFirst)keyboard.OnTextInput(text[i]);else p.controls.GuiText(input);
        }
    }
    public static IEnumerator Run(Plugin p,string directory)
    {
        output=directory;failures=0;File.WriteAllText(Path.Combine(output,"typing.txt"),"");
        var previous=Keyboard.current;
        var keyboard=InputSystem.AddDevice<Keyboard>("LethalCraft typing fixture");keyboard.MakeCurrent();p.controls.RefreshKeyboard();
        for(int test=0;test<3;test++)
        {
            if(test==1)
            {
                InputSystem.RemoveDevice(keyboard);
                keyboard=InputSystem.AddDevice<Keyboard>("LethalCraft replacement keyboard");keyboard.MakeCurrent();p.controls.RefreshKeyboard();
            }
            if(test==2)
            {
                p.Link.Input(90,2);yield return new WaitForSecondsRealtime(.5f);
                p.Link.Input(10);yield return Wait(()=>p.MinecraftMenu,"creative inventory opened");
                p.Link.Input(90,3);yield return new WaitForSecondsRealtime(.4f);
            }
            else {Key(p,23);yield return Wait(()=>p.MinecraftMenu,"chat opened "+test);}
            string expected=test==0?"bookkeeper 1122 !? é😀":test==1?"Mississippi 0000 café":"bookkeeper";
            int before=p.controls.TextCharactersSent;
            yield return Both(p,keyboard,expected,test!=1);
            ushort snapshot=(ushort)(70+test);p.Link.Input(90,snapshot);
            string path=Path.Combine(output,"minecraft-check-"+snapshot+".txt");
            yield return Wait(()=>File.Exists(path),"Minecraft text snapshot "+test);
            Check(File.Exists(path)&&File.ReadAllLines(path).Contains("text="+expected),"exact text without duplicates "+test);
            Check(p.controls.TextCharactersSent-before==expected.Count(c=>!char.IsLowSurrogate(c)),"one bridge event per Unicode character "+test);
            Key(p,41);yield return Wait(()=>!p.MinecraftMenu,"text screen closed "+test);
        }
        int count=p.controls.TextCharactersSent;
        keyboard.OnTextInput('x');p.controls.GuiText(new Event{type=EventType.KeyDown,character='x'});
        Check(p.controls.TextCharactersSent==count,"closed text screens do not forward characters");
        InputSystem.RemoveDevice(keyboard);previous?.MakeCurrent();p.controls.RefreshKeyboard();
        File.AppendAllText(Path.Combine(output,"typing.txt"),"Failures: "+failures+"\n");
    }
}
