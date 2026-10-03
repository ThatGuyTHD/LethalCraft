using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace LethalCraft;

internal static class RegressionTest
{
    static void Key(Plugin p,ushort code){p.Link.Input(1,code,1);p.Link.Input(1,code,0);}
    static void Type(string text){foreach(char c in text)Keyboard.current.OnTextInput(c);}
    static void Note(string output,string text)=>File.AppendAllText(Path.Combine(output,"regressions.txt"),text+"\n");
    public static void MeshIndexChecks(string output)
    {
        var vertices=new Vector3[65*65];var indices=new int[64*64*6];int next=0;
        for(int z=0;z<=64;z++)for(int x=0;x<=64;x++)vertices[z*65+x]=new Vector3(x,Mathf.Sin(x*.2f)*Mathf.Cos(z*.3f),z);
        for(int z=0;z<64;z++)for(int x=0;x<64;x++)
        {int a=z*65+x;indices[next++]=a;indices[next++]=a+65;indices[next++]=a+1;indices[next++]=a+1;indices[next++]=a+65;indices[next++]=a+66;}
        var mesh=new MeshIndex(vertices,indices);var candidates=new System.Collections.Generic.List<int>();
        var random=new System.Random(713);bool complete=true;int checkedTriangles=0;
        for(int q=0;q<25;q++)
        {
            var lo=new Vector3(random.Next(0,57),-2,random.Next(0,57));var hi=lo+new Vector3(8,4,8);
            mesh.Query(lo,hi,candidates);var found=new System.Collections.Generic.HashSet<int>(candidates);
            for(int i=0;i<indices.Length;i+=3)
                if(new Triangle(vertices[indices[i]],vertices[indices[i+1]],vertices[indices[i+2]]).Touches(lo,hi)&&!found.Contains(i))complete=false;
            checkedTriangles+=candidates.Count;
        }
        Note(output,$"bvhMatchesBruteForce={complete}\nbvhTestCandidates={checkedTriangles}\nbvhBruteForceTriangles={mesh.TriangleCount*25}");
        if(!complete)throw new InvalidOperationException("Collision BVH omitted intersecting triangles");
    }
    public static IEnumerator InputChecks(Plugin p,string output)
    {
        MeshIndexChecks(output);
        // Exercise the registered Unity text callback, including a keyboard connected after Awake.
        var previousKeyboard=Keyboard.current;
        var keyboard=InputSystem.AddDevice<Keyboard>("LethalCraft test keyboard");
        p.controls.RefreshKeyboard();
        Key(p,23);yield return new WaitForSecondsRealtime(.5f);Type("Lethal chat 123");
        p.Link.Input(90,1);yield return new WaitForSecondsRealtime(.5f);
        Note(output,"chatText="+Read(output,1).Contains("text=Lethal chat 123"));
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"10-chat-text.png"));
        Key(p,41);yield return new WaitForSecondsRealtime(.4f);
        p.Link.Input(90,2);yield return new WaitForSecondsRealtime(.6f);
        p.Link.Input(10);yield return new WaitForSecondsRealtime(.8f);
        p.Link.Input(90,3);yield return new WaitForSecondsRealtime(.5f);Type("diamond");
        p.Link.Input(90,4);yield return new WaitForSecondsRealtime(.5f);
        Note(output,"creativeSearchText="+Read(output,4).Contains("text=diamond"));
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"11-search-text.png"));
        Key(p,41);yield return new WaitForSecondsRealtime(.4f);
        p.Link.Input(90,7);yield return new WaitForSecondsRealtime(.5f);
        p.Link.Input(90,5);yield return new WaitForSecondsRealtime(1);
        p.Link.Input(90,6);yield return new WaitForSecondsRealtime(.8f);p.Link.Input(90,8);
        yield return new WaitForSecondsRealtime(.4f);
        Note(output,"minecraftEChest="+Read(output,8).Contains("ContainerScreen"));
        Key(p,41);yield return new WaitForSecondsRealtime(.4f);
        p.Link.Input(90,9);yield return new WaitForSecondsRealtime(.4f);
        // The actual native E action reaches an InteractTrigger and never opens MC inventory.
        var player=Plugin.LocalPlayer!;
        var probe=new GameObject("LethalCraft E regression trigger");probe.transform.position=player.gameplayCamera.transform.position+player.gameplayCamera.transform.forward;
        probe.layer=9;probe.tag="InteractTrigger";probe.AddComponent<BoxCollider>().size=Vector3.one*.4f;
        var trigger=probe.AddComponent<InteractTrigger>();trigger.interactCooldown=false;trigger.hoverTip="Test : [E]";
        trigger.onInteract=new InteractEvent();trigger.onInteractEarly=new InteractEvent();trigger.onStopInteract=new InteractEvent();
        trigger.onCancelAnimation=new InteractEvent();trigger.onInteractEarlyOtherClients=new InteractEvent();trigger.holdingInteractEvent=new InteractEventFloat();
        int interactions=0;trigger.onInteract.AddListener(_=>interactions++);
        yield return new WaitForSecondsRealtime(.4f);player.hoveringOverTrigger=trigger;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(UnityEngine.InputSystem.Key.E));yield return null;
        p.controls.Poll();yield return new WaitForSecondsRealtime(.3f);
        InputSystem.QueueStateEvent(keyboard,new KeyboardState());yield return null;p.controls.Poll();
        Note(output,$"nativeEInteractions={interactions}\nnativeEDidNotOpenInventory={!p.Minecraft.MenuOpen}");
        player.hoveringOverTrigger=null;UnityEngine.Object.Destroy(probe);
        InputSystem.RemoveDevice(keyboard);previousKeyboard?.MakeCurrent();p.controls.RefreshKeyboard();
        Note(output,$"textCharactersSent={p.controls.TextCharactersSent}");
    }
    static string Read(string output,int check)
    {var path=Path.Combine(output,"minecraft-check-"+check+".txt");return File.Exists(path)?File.ReadAllText(path):"missing";}

    public static IEnumerator SprintChecks(Plugin p,string output)
    {
        Key(p,62);yield return new WaitForSecondsRealtime(.4f); // F5 shows only the Minecraft character.
        p.Link.Input(1,224,1);p.Link.Input(1,26,1);
        double maxOffset=0;float until=Time.realtimeSinceStartup+1;
        while(Time.realtimeSinceStartup<until)
        {
            yield return new WaitForEndOfFrame();
            maxOffset=Math.Max(maxOffset,Vector3.Distance(Plugin.LocalPlayer!.transform.position,Coordinates.ToUnity(p.Minecraft.X,p.Minecraft.Y,p.Minecraft.Z)));
        }
        p.Link.Input(1,26,0);p.Link.Input(1,224,0);
        var player=Plugin.LocalPlayer!;
        bool hidden=player.GetComponentsInChildren<Renderer>().Where(r=>r.GetComponentInParent<GrabbableObject>()==null).All(r=>r.forceRenderingOff);
        int colliders=player.GetComponentsInChildren<Collider>().Count(c=>c.enabled&&c.GetComponentInParent<GrabbableObject>()==null);
        Note(output,$"nativeRenderersHidden={hidden}\nnativePlayerColliderCount={colliders}\nsprintPositionOffset={maxOffset:0.000000}");
        yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"12-sprint.png"));
        Key(p,62);yield return new WaitForSecondsRealtime(.3f);Key(p,62);yield return new WaitForSecondsRealtime(.4f);
    }

    public static IEnumerator ArrowCheck(Plugin p,EnemyAI enemy,string output)
    {
        Key(p,32);yield return new WaitForSecondsRealtime(.5f); // Starter bow, slot 3.
        int hp=enemy.enemyHP;
        enemy.SetEnemyStunned(true,8,Plugin.LocalPlayer);
        Vector3 aim=CombatBridge.BodyBounds(enemy).center-Plugin.LocalPlayer!.gameplayCamera.transform.position;
        p.Yaw=Mathf.Atan2(aim.x,aim.z)*Mathf.Rad2Deg;p.Pitch=-Mathf.Atan2(aim.y,new Vector2(aim.x,aim.z).magnitude)*Mathf.Rad2Deg;
        yield return new WaitForSecondsRealtime(.4f);
        p.Link.Input(2,3,1);yield return new WaitForSecondsRealtime(1.25f);p.Link.Input(2,3,0);
        bool flashed=false;float until=Time.realtimeSinceStartup+1.5f;
        while(Time.realtimeSinceStartup<until)
        {
            if(!flashed&&enemy.GetComponent<HitFlash>()?.Showing==true)
            {
                flashed=true;yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,"13-arrow-hit-red.png"));
            }
            yield return null;
        }
        Note(output,$"arrowEnemyHpBefore={hp}\narrowEnemyHpAfter={enemy.enemyHP}\narrowDamagedEnemy={enemy.enemyHP<hp}\nredHitFlash={flashed}");
        Key(p,30);yield return new WaitForSecondsRealtime(.5f);
    }
}
