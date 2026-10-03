using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GameNetcodeStuff;
using LethalCraft.Bridge;
using UnityEngine;

namespace LethalCraft;

internal sealed class CombatBridge : IDisposable
{
    readonly Plugin plugin;
    readonly Dictionary<uint,EnemyAI> actors=new();
    readonly Dictionary<uint,float> fractions=new();
    float nextScan;
    bool applying;
    internal bool NativeBlastHandled;
    internal Vector3? AttackOrigin;
    readonly Dictionary<EnemyAI,HitFlash> flashes=new();
    public CombatBridge(Plugin plugin)=>this.plugin=plugin;
    public void Tick(PlayerControllerB player)
    {
        if(Time.unscaledTime>=nextScan)
        {
            nextScan=Time.unscaledTime+.05f;actors.Clear();using var stream=new MemoryStream();using var w=new BinaryWriter(stream);
            var enemies=RoundManager.Instance?.SpawnedEnemies;
            if(enemies!=null)foreach(EnemyAI enemy in enemies)
            {
                if(actors.Count>=256)break;
                if(enemy==null||!enemy.isActiveAndEnabled||!enemy.IsSpawned||enemy.NetworkObjectId>uint.MaxValue)continue;
                if(plugin.multiplayer.Role!=1&&(enemy.transform.position-player.transform.position).sqrMagnitude>4096)continue;
                uint id=(uint)enemy.NetworkObjectId;actors[id]=enemy;
                Bounds bounds=BodyBounds(enemy);
                Vector3 feet=Coordinates.ToMinecraft(new Vector3(bounds.center.x,bounds.min.y,bounds.center.z));
                w.Write(id);w.Write(1u|(enemy.isEnemyDead?2u:0u)|(!enemy.enemyType.canDie?4u:0u));
                w.Write(feet.x);w.Write(feet.y);w.Write(feet.z);w.Write(enemy.transform.eulerAngles.y);
                w.Write(Mathf.Clamp(Math.Max(bounds.size.x,bounds.size.z)+.2f,.2f,8));w.Write(Mathf.Clamp(bounds.size.y+.1f,.2f,12));
                w.Write(enemy.isEnemyDead?0f:1f);w.Write((ushort)1);w.Write((ushort)0);
                byte[] name=Encoding.UTF8.GetBytes(enemy.enemyType.enemyName??enemy.name),record=new byte[24];
                Array.Copy(name,record,Math.Min(name.Length,23));w.Write(record);
            }
            plugin.Link.PublishActors(stream.ToArray());
        }
        if(plugin.Possessed&&plugin.Ready&&plugin.Minecraft.MaxHealth>0&&!player.isPlayerDead)
        {
            int health=Mathf.Clamp(Mathf.CeilToInt(plugin.Minecraft.Health/plugin.Minecraft.MaxHealth*100),0,100);
            if(player.health!=health)
            {
                applying=true;
                try
                {
                    if(health<player.health)player.DamagePlayer(player.health-health);
                    player.health=health;HUDManager.Instance?.UpdateHealthUI(health);
                    if(health>20&&player.criticallyInjured)player.MakeCriticallyInjured(false);
                }
                finally{applying=false;}
            }
        }
    }
    internal static Bounds BodyBounds(EnemyAI enemy)
    {
        Bounds result=new(enemy.transform.position+Vector3.up,new Vector3(.7f,2,.7f));float best=0;
        foreach(Collider collider in enemy.GetComponentsInChildren<Collider>())
        {
            if(!collider.enabled||collider.GetComponent<ScanNodeProperties>()!=null||collider.name.IndexOf("ScanNode",StringComparison.OrdinalIgnoreCase)>=0)continue;
            Vector3 size=collider.bounds.size;
            if(size.x>8||size.z>8||size.y>12)continue;
            float volume=size.x*size.y*size.z;
            if(volume>best){best=volume;result=collider.bounds;}
        }
        return result;
    }
    public bool Damage(PlayerControllerB player,int damage,CauseOfDeath cause,bool fall,Vector3 force)
    {
        if(applying||player!=Plugin.LocalPlayer||!plugin.Possessed||damage<=0)return true;
        if(fall||cause==CauseOfDeath.Gravity)return false; // Native fall calls do not consistently set the fallDamage argument.
        if(cause==CauseOfDeath.Blast&&NativeBlastHandled)return false;
        ushort kind=3;uint attacker=0;
        Vector3? origin=AttackOrigin;
        if(origin.HasValue)kind=0;
        if(cause==CauseOfDeath.Mauling||cause==CauseOfDeath.Bludgeoning||cause==CauseOfDeath.Gunshots)
        {
            kind=(ushort)(cause==CauseOfDeath.Gunshots?1:0);float nearest=25;
            foreach(var pair in actors)
            {
                if(pair.Value==null||pair.Value.isEnemyDead)continue;
                float distance=(pair.Value.transform.position-player.transform.position).sqrMagnitude;
                if(distance<nearest){attacker=pair.Key;nearest=distance;if(!AttackOrigin.HasValue)origin=pair.Value.transform.position;}
            }
        }
        if(cause==CauseOfDeath.Blast)kind=4;
        if(!origin.HasValue&&force.sqrMagnitude>.001f&&(cause==CauseOfDeath.Blast||cause==CauseOfDeath.Gunshots))origin=player.transform.position-force.normalized*2;
        if(origin.HasValue)return !DirectionalDamage(player,damage,kind,origin.Value);
        return !plugin.Link.Input(7,kind,checked(Math.Min(damage,10000)*100),unchecked((int)attacker));
    }
    bool DirectionalDamage(PlayerControllerB player,int damage,ushort kind,Vector3 origin)
    {
        Vector3 direction=Coordinates.ToMinecraft(origin-player.transform.position);
        // Flag 4 changes b from an actor id to the source bearing, in radians. One atomic
        // input record keeps origin and damage together, including across the guest relay.
        float bearing=Mathf.Atan2(direction.x,direction.z);
        return plugin.Link.Input(7,kind,Math.Min(Math.Max(damage,0),10000)*100,BitConverter.SingleToInt32Bits(bearing),4);
    }
    internal void NativeExplosion(Vector3 centre,float killRange,float damageRange,int damage,bool throughCar)
    {
        var player=Plugin.LocalPlayer;
        if(player==null||player.isPlayerDead||!plugin.Possessed||!plugin.Ready)return;
        float distance=Vector3.Distance(centre,player.transform.position);
        if(distance>=Math.Max(killRange,damageRange))return;
        if(Physics.Linecast(centre,player.transform.position+Vector3.up*.3f,out var hit,1073742080,QueryTriggerInteraction.Ignore)
            &&((!throughCar&&hit.collider.gameObject.layer==30)||distance>4))return;
        // The native blast's lethal branch bypasses DamagePlayer. Route it before the
        // collider loop so an aligned MC capsule and a raised shield get the same hit.
        NativeBlastHandled=DirectionalDamage(player,distance<killRange?100:damage,4,centre);
    }
    public bool NativeDeath(PlayerControllerB player,CauseOfDeath cause,Vector3 force)
    {
        if(!applying&&player==Plugin.LocalPlayer&&plugin.Possessed&&cause==CauseOfDeath.Blast)
        {
            if(NativeBlastHandled)return false;
            return !DirectionalDamage(player,100,4,player.transform.position-force.normalized*2);
        }
        if(!applying&&player==Plugin.LocalPlayer&&plugin.Link.Connected&&plugin.Minecraft.InWorld)plugin.Link.Input(7,3,1000000);
        return true;
    }
    public void Event(GameEvent e)
    {
        PlayerControllerB? p=Plugin.LocalPlayer;if(p==null)return;
        if(e.Type==1&&actors.TryGetValue(e.Actor,out EnemyAI enemy)&&enemy!=null&&!enemy.isEnemyDead&&enemy.enemyType.canDie)
        {
            if(!Protocol.Finite(e.A)||e.A<=0)return;
            float amount=(fractions.TryGetValue(e.Actor,out float remainder)?remainder:0)+Math.Min(e.A,1000)/7f*plugin.WeaponDamageMultiplier;
            int force=(int)amount;
            // A normal arrow used to deal less than one native hit, so its first impact did nothing.
            if((e.Flags&2)!=0)force=Math.Max(force,1);
            fractions[e.Actor]=Math.Max(0,amount-force);
            if(force>0)enemy.HitEnemyOnLocalClient(force,Coordinates.ToUnity(new Vector3(e.B,0,e.C)),p,true);
            Flash(e.Actor);plugin.multiplayer.Flash(e.Actor);
        }
        else if(e.Type==2&&!p.isPlayerDead)
        {
            applying=true;try{p.KillPlayer(Vector3.zero);}finally{applying=false;}
        }
        else if(e.Type==3&&Protocol.Finite(e.A)&&Protocol.Finite(e.B)&&Protocol.Finite(e.C)&&Protocol.Finite(e.D)&&e.D>0&&e.D<128)
        {
            Vector3 centre=Coordinates.ToUnity(e.A,e.B,e.C);
            foreach(Collider collider in Physics.OverlapSphere(centre,e.D,~0,QueryTriggerInteraction.Ignore))
                if(collider.attachedRigidbody!=null)collider.attachedRigidbody.AddExplosionForce(e.D*60,centre,e.D,1);
            RoundManager.Instance?.PlayAudibleNoise(centre,e.D*4,1);
        }
    }
    public void Dispose()
    {
        foreach(var flash in flashes.Values)if(flash!=null)UnityEngine.Object.Destroy(flash);
        flashes.Clear();
    }
    internal void Flash(uint actor)
    {
        if(!actors.TryGetValue(actor,out var enemy)||enemy==null)return;
        if(!flashes.TryGetValue(enemy,out var flash)||flash==null){flash=enemy.gameObject.AddComponent<HitFlash>();flashes[enemy]=flash;}
        flash.Show();
    }
}

internal sealed class HitFlash : MonoBehaviour
{
    readonly System.Collections.Generic.List<(Renderer renderer,int slot,MaterialPropertyBlock original,MaterialPropertyBlock red)> slots=new();
    float until;
    bool shown;
    public bool Showing=>shown;
    void Awake()
    {
        foreach(var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if(!(renderer is SkinnedMeshRenderer)&&!(renderer is MeshRenderer))continue;
            var materials=renderer.sharedMaterials;
            for(int i=0;i<materials.Length;i++)
            {
                var material=materials[i];if(material==null)continue;
                var original=new MaterialPropertyBlock();renderer.GetPropertyBlock(original,i);
                var red=new MaterialPropertyBlock();renderer.GetPropertyBlock(red,i);
                red.SetColor("_BaseColor",new Color(1,.16f,.16f,1));
                red.SetColor("_Color",new Color(1,.16f,.16f,1));
                red.SetColor("_EmissiveColor",new Color(.08f,.001f,.001f,1));
                red.SetColor("_EmissionColor",new Color(.08f,.001f,.001f,1));
                slots.Add((renderer,i,original,red));
            }
        }
    }
    public void Show(){until=Time.unscaledTime+.22f;shown=true;Apply();}
    void Apply(){foreach(var slot in slots)if(slot.renderer!=null)slot.renderer.SetPropertyBlock(slot.red,slot.slot);}
    void LateUpdate(){if(!shown)return;if(Time.unscaledTime<until)Apply();else Restore();}
    void Restore(){foreach(var slot in slots)if(slot.renderer!=null)slot.renderer.SetPropertyBlock(slot.original.isEmpty?null:slot.original,slot.slot);shown=false;}
    void OnDisable(){if(shown)Restore();}
    void OnDestroy(){if(shown)Restore();}
}
