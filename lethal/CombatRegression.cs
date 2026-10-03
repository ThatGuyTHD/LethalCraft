using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace LethalCraft;

// Only reached by the explicit smoke-test switch, in its own save/world.
internal static class CombatRegression
{
    public static IEnumerator Run(Plugin plugin,string output)
    {
        string report=Path.Combine(output,"combat.txt");
        void Note(string key,object value){File.AppendAllText(report,$"{key}={value}\n");Plugin.Log.LogInfo($"COMBAT: {key}={value}");}
        var player=Plugin.LocalPlayer!;
        plugin.Link.Input(90,40);yield return new WaitForSecondsRealtime(2);
        plugin.Yaw=0;plugin.Pitch=0;
        yield return new WaitForSecondsRealtime(1);
        // Native blasts use the real LC explosion function, including its lethal branch.
        for(int check=0;check<5;check++)
        {
            plugin.Link.Input(90,40);yield return new WaitForSecondsRealtime(1);
            if(check!=2){plugin.Link.Input(2,3,1);yield return new WaitForSecondsRealtime(.6f);}
            Vector3 origin=player.transform.position+Vector3.forward*(check==1?-2:check==3?12:2);
            float before=plugin.Minecraft.Health;
            Landmine.SpawnExplosion(origin,false,check==4?3:0,5,30);
            yield return new WaitForSecondsRealtime(.8f);
            Note(new[]{"blastFront","blastBack","blastLowered","blastOutOfRange","blastLethalBlocked"}[check],$"{before}->{plugin.Minecraft.Health}; native={player.health}; dead={player.isPlayerDead}");
            plugin.Link.Input(90,(ushort)(50+check));yield return new WaitForSecondsRealtime(.3f);
            plugin.Link.Input(2,3,0);
        }
        // The native monster hit path must resolve the enemy's position for shield direction.
        plugin.Link.Input(90,41);yield return new WaitForSecondsRealtime(1);
        var type=Resources.FindObjectsOfTypeAll<EnemyType>().First(e=>e.enemyName=="Hoarding bug");
        Vector3 enemyPosition=player.transform.position+Vector3.forward*2;
        var reference=RoundManager.Instance.SpawnEnemyGameObject(enemyPosition,0,-1,type);
        yield return new WaitForSecondsRealtime(1);
        if(!reference.TryGet(out var obj)){Note("enemySpawned",false);yield break;}
        var enemy=obj.GetComponent<EnemyAI>();enemy.enemyHP=100;enemy.SetEnemyStunned(true,120,player);
        if(enemy.agent!=null)enemy.agent.enabled=false;
        for(int check=0;check<3;check++)
        {
            enemy.transform.position=player.transform.position+Vector3.forward*(check==1?-2:2);
            plugin.Link.Input(90,40);yield return new WaitForSecondsRealtime(1);
            if(check!=2){plugin.Link.Input(2,3,1);yield return new WaitForSecondsRealtime(.6f);}
            float before=plugin.Minecraft.Health;player.DamagePlayer(30,causeOfDeath:CauseOfDeath.Mauling);
            yield return new WaitForSecondsRealtime(.8f);
            Note(new[]{"meleeFront","meleeBack","meleeLowered"}[check],$"{before}->{plugin.Minecraft.Health}; native={player.health}");
            plugin.Link.Input(90,(ushort)(55+check));yield return new WaitForSecondsRealtime(.3f);plugin.Link.Input(2,3,0);
        }
        plugin.Link.Input(90,41);yield return new WaitForSecondsRealtime(1);
        enemy.transform.position=player.transform.position+Vector3.forward*2;
        int hp=enemy.enemyHP;plugin.Link.Input(90,42);yield return new WaitForSecondsRealtime(2);
        Note("tntEnemyDamage",$"{hp}->{enemy.enemyHP}");
        hp=enemy.enemyHP;plugin.SetNativeMode(true);yield return new WaitForSecondsRealtime(1);
        plugin.Link.Input(90,42);yield return new WaitForSecondsRealtime(2);
        Note("tntEnemyWhileNative",$"{hp}->{enemy.enemyHP}");
        plugin.SetNativeMode(false);yield return new WaitForSecondsRealtime(1);
        plugin.Link.Input(90,43);yield return new WaitForSecondsRealtime(1);
        float health=plugin.Minecraft.Health;plugin.Link.Input(90,44);yield return new WaitForSecondsRealtime(2);
        Note("tntPlayerDamage",$"{health}->{plugin.Minecraft.Health}; native={player.health}");
        plugin.Link.Input(90,58);yield return new WaitForSecondsRealtime(.5f);
        Note("complete",true);
    }
}
