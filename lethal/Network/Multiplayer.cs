using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using LethalCraft.Bridge;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace LethalCraft.Network;

// Minecraft's authenticated TCP connection travels over the existing LC Steam/LAN transport.
// Both TCP endpoints bind only to loopback; guests cannot select an arbitrary destination.
internal sealed class Multiplayer : IDisposable
{
    const string Message="LethalCraft.Relay.1";
    readonly Plugin plugin;
    NetworkManager? manager;
    readonly ConcurrentQueue<Action> completed=new();
    readonly ConcurrentQueue<(ulong peer,RelayFrame frame)> outgoing=new();
    readonly Dictionary<(ulong,uint),RelayStream> streams=new();
    readonly HashSet<(ulong,uint)> connecting=new();
    readonly HashSet<ulong> peers=new();
    readonly Dictionary<ulong,bool> modes=new();
    readonly Dictionary<GameNetcodeStuff.PlayerControllerB,Appearance.HiddenRenderers> remoteRenderers=new();
    readonly List<Transform> remoteRoots=new();
    TcpListener? listener;
    uint epoch, nextStream;
    string hostWorld="";
    float nextHello, nextOffer, started;
    bool lastMode;
    public uint Role=>manager==null?0u:manager.IsServer?1u:2u;
    public int LocalPort { get; private set; }
    public string GuestWorld { get; private set; }="";
    public uint GuestEpoch=>epoch;
    public string Status { get; private set; }="";
    public int ConnectedPeers=>peers.Count;
    public long BytesSent { get; private set; }
    public long BytesReceived { get; private set; }
    public Multiplayer(Plugin plugin)=>this.plugin=plugin;
    public void Tick()
    {
        var current=NetworkManager.Singleton;
        if(current==null||!current.IsListening)current=null;
        if(current!=manager)
        {
            Dispose();manager=current;started=Time.unscaledTime;
            if(manager!=null){manager.CustomMessagingManager.RegisterNamedMessageHandler(Message,Received);Status=manager.IsServer?"Hosting shared Minecraft world":"Connecting to host's Minecraft world";}
        }
        while(completed.TryDequeue(out var action))action();
        if(manager==null)return;
        if(manager.IsServer)
        {
            string name=plugin.saves.WorldName;
            if(hostWorld!=name){hostWorld=name;ResetStreams();epoch=BitConverter.ToUInt32(Guid.NewGuid().ToByteArray(),0)|1u;foreach(ulong id in peers)modes[id]=false;nextOffer=0;}
            foreach(ulong peer in peers.ToArray())if(!manager.ConnectedClients.ContainsKey(peer))RemovePeer(peer);
            if(Time.unscaledTime>=nextOffer){nextOffer=Time.unscaledTime+1;foreach(ulong peer in peers)Offer(peer);BroadcastModes();}
        }
        else if(Time.unscaledTime>=nextHello)
        {
            nextHello=Time.unscaledTime+1;
            Enqueue(NetworkManager.ServerClientId,new RelayFrame{Kind=RelayKind.Hello,Data=BitConverter.GetBytes(Protocol.Version)});
            if(GuestWorld.Length==0&&Time.unscaledTime-started>15)Status="Waiting for host: everyone needs LethalCraft 0.2.2";
        }
        int budget=128*1024;
        while(budget>0&&outgoing.TryDequeue(out var entry))
        {
            if(entry.frame.Kind!=RelayKind.Hello&&entry.frame.Epoch!=epoch)continue;
            if(manager.IsServer&&!manager.ConnectedClients.ContainsKey(entry.peer))continue;
            byte[] bytes=entry.frame.Encode();
            using var writer=new FastBufferWriter(bytes.Length,Allocator.Temp);
            writer.WriteBytesSafe(bytes);
            manager.CustomMessagingManager.SendNamedMessage(Message,entry.peer,writer,NetworkDelivery.ReliableFragmentedSequenced);
            BytesSent+=bytes.Length;budget-=bytes.Length;
        }
        foreach(var pair in streams.Where(p=>p.Value.Closed).ToArray()){pair.Value.Dispose();streams.Remove(pair.Key);}
    }
    void Enqueue(ulong peer,RelayFrame frame)=>outgoing.Enqueue((peer,frame));
    void Offer(ulong peer)
    {
        bool available=plugin.Link.Connected&&plugin.SessionReady&&plugin.Minecraft.ServerPort>0;
        Enqueue(peer,new RelayFrame{Kind=RelayKind.Offer,Epoch=epoch,Data=Encoding.ASCII.GetBytes(available?hostWorld:"")});
    }
    void Received(ulong sender,FastBufferReader reader)
    {
        try
        {
            if(manager==null||(!manager.IsServer&&sender!=NetworkManager.ServerClientId)||
                (manager.IsServer&&(!manager.ConnectedClients.ContainsKey(sender)||sender==manager.LocalClientId)))return;
            int length=reader.Length-reader.Position;if(length<RelayFrame.Header||length>RelayFrame.Header+RelayFrame.MaxData)return;
            byte[] bytes=new byte[length];reader.ReadBytesSafe(ref bytes,length);
            var frame=RelayFrame.Decode(bytes);BytesReceived+=length;
            if(frame.Kind==RelayKind.Hello&&manager.IsServer)
            {
                if(frame.Data.Length!=4||BitConverter.ToUInt32(frame.Data,0)!=Protocol.Version){Status="Multiplayer version mismatch; update every PC";return;}
                if(peers.Add(sender))Plugin.Log.LogInfo("LethalCraft peer connected: "+sender);
                Offer(sender);return;
            }
            if(frame.Kind==RelayKind.Offer&&!manager.IsServer)
            {
                string name=Encoding.ASCII.GetString(frame.Data);
                if(name.Length>95||name.Length>0&&!System.Text.RegularExpressions.Regex.IsMatch(name,"^LethalCraft_[A-Za-z0-9_-]+$"))return;
                if(epoch!=frame.Epoch||GuestWorld!=name)
                {
                    ResetStreams();epoch=frame.Epoch;GuestWorld=name;
                    if(name.Length>0)StartGuestListener();
                }
                Status=name.Length>0?"Joining host's Minecraft world":"Host is loading Minecraft";return;
            }
            if(frame.Epoch!=epoch||manager.IsServer&&!peers.Contains(sender))return;
            if(frame.Kind==RelayKind.Mode&&manager.IsServer&&frame.Data.Length==1)
            {modes[sender]=frame.Data[0]!=0;BroadcastModes();return;}
            if(frame.Kind==RelayKind.Modes&&!manager.IsServer)
            {
                using var buffer=new MemoryStream(frame.Data);using var input=new BinaryReader(buffer);
                int count=input.ReadByte();if(count>16||frame.Data.Length!=1+count*9)return;
                modes.Clear();for(int i=0;i<count;i++)modes[input.ReadUInt64()]=input.ReadBoolean();return;
            }
            if(frame.Kind==RelayKind.Flash&&frame.Data.Length==4)
            {
                plugin.FlashEnemy(BitConverter.ToUInt32(frame.Data,0));
                if(manager.IsServer)foreach(ulong peer in peers)if(peer!=sender)Enqueue(peer,frame);
                return;
            }
            var key=(sender,frame.Stream);
            if(frame.Kind==RelayKind.Open&&manager.IsServer)
            {
                if(frame.Stream==0||!plugin.SessionReady||plugin.Minecraft.ServerPort==0||streams.Keys.Count(k=>k.Item1==sender)+connecting.Count(k=>k.Item1==sender)>=2)return;
                if(streams.ContainsKey(key)||!connecting.Add(key))return;
                int port=(int)plugin.Minecraft.ServerPort;uint captured=epoch;
                _=ConnectHost(sender,frame.Stream,port,captured);return;
            }
            if(streams.TryGetValue(key,out var stream))stream.Receive(frame);
        }
        catch(Exception e) when(e is IOException||e is ArgumentException||e is InvalidOperationException)
        {Status="Multiplayer connection error: "+e.Message;Plugin.Log.LogWarning(Status);}
    }
    async Task ConnectHost(ulong peer,uint id,int port,uint captured)
    {
        var tcp=new TcpClient();
        try
        {
            var connect=tcp.ConnectAsync(IPAddress.Loopback,port);
            if(await Task.WhenAny(connect,Task.Delay(5000)).ConfigureAwait(false)!=connect)throw new IOException("Minecraft host did not accept the local connection");
            await connect.ConfigureAwait(false);
            completed.Enqueue(()=>{connecting.Remove((peer,id));if(manager==null||epoch!=captured||!peers.Contains(peer)){tcp.Dispose();return;}Attach(peer,id,tcp,captured);Enqueue(peer,new RelayFrame{Kind=RelayKind.Opened,Epoch=captured,Stream=id});});
        }
        catch(Exception e) when(e is SocketException||e is IOException||e is ObjectDisposedException)
        {
            tcp.Dispose();completed.Enqueue(()=>{connecting.Remove((peer,id));Enqueue(peer,new RelayFrame{Kind=RelayKind.Abort,Epoch=captured,Stream=id});});
        }
    }
    void StartGuestListener()
    {
        listener=new TcpListener(IPAddress.Loopback,0);listener.Start(2);LocalPort=((IPEndPoint)listener.LocalEndpoint).Port;
        var current=listener;uint captured=epoch;
        _=Task.Run(async()=>{
            try
            {
                while(current==listener)
                {
                    var tcp=await current.AcceptTcpClientAsync().ConfigureAwait(false);
                    completed.Enqueue(()=>{
                        if(manager==null||epoch!=captured||current!=listener||streams.Count>=2){tcp.Dispose();return;}
                        uint id=++nextStream;Enqueue(NetworkManager.ServerClientId,new RelayFrame{Kind=RelayKind.Open,Epoch=captured,Stream=id});
                        Attach(NetworkManager.ServerClientId,id,tcp,captured,true);
                    });
                }
            }
            catch(Exception e) when(e is SocketException||e is ObjectDisposedException){}
        });
    }
    void Attach(ulong peer,uint id,TcpClient tcp,uint captured,bool wait=false)
    {
        streams[(peer,id)]=new RelayStream(tcp,frame=>{frame.Epoch=captured;frame.Stream=id;Enqueue(peer,frame);},wait);
    }
    public void PublishMode(bool mode)
    {
        if(manager==null)return;
        modes[manager.LocalClientId]=mode;
        if(mode==lastMode)return;lastMode=mode;
        if(manager.IsServer)BroadcastModes();
        else Enqueue(NetworkManager.ServerClientId,new RelayFrame{Kind=RelayKind.Mode,Epoch=epoch,Data=new[]{mode?(byte)1:(byte)0}});
    }
    void BroadcastModes()
    {
        if(manager==null||!manager.IsServer)return;
        using var buffer=new MemoryStream();using var writer=new BinaryWriter(buffer);
        writer.Write((byte)Math.Min(modes.Count,16));foreach(var pair in modes.Take(16)){writer.Write(pair.Key);writer.Write(pair.Value);}
        foreach(ulong peer in peers)Enqueue(peer,new RelayFrame{Kind=RelayKind.Modes,Epoch=epoch,Data=buffer.ToArray()});
    }
    public void Flash(uint actor)
    {
        if(manager==null)return;var frame=new RelayFrame{Kind=RelayKind.Flash,Epoch=epoch,Data=BitConverter.GetBytes(actor)};
        if(manager.IsServer){foreach(ulong peer in peers)Enqueue(peer,frame);}else Enqueue(NetworkManager.ServerClientId,frame);
    }
    public void ApplyRemoteAppearance()
    {
        var players=StartOfRound.Instance?.allPlayerScripts;if(players==null)return;
        foreach(var player in players)
        {
            if(player==null||player==Plugin.LocalPlayer)continue;
            if(!remoteRenderers.TryGetValue(player,out var visibility))remoteRenderers[player]=visibility=new Appearance.HiddenRenderers();
            remoteRoots.Clear();
            if(player.isPlayerControlled&&!player.isPlayerDead&&modes.TryGetValue(player.actualClientId,out bool active)&&active)
            {
                remoteRoots.Add(player.transform);
                if(player.currentlyHeldObjectServer!=null)remoteRoots.Add(player.currentlyHeldObjectServer.transform);
            }
            visibility.Set(remoteRoots);
        }
    }
    void RemovePeer(ulong peer)
    {
        peers.Remove(peer);modes.Remove(peer);
        foreach(var pair in streams.Where(p=>p.Key.Item1==peer).ToArray()){pair.Value.Dispose();streams.Remove(pair.Key);}
        foreach(var pair in remoteRenderers.Where(p=>p.Key==null||p.Key.actualClientId==peer).ToArray()){pair.Value.Restore();remoteRenderers.Remove(pair.Key);}
    }
    void ResetStreams()
    {
        var previous=listener;listener=null;previous?.Stop();LocalPort=0;
        foreach(var stream in streams.Values)stream.Dispose();streams.Clear();connecting.Clear();
        while(outgoing.TryDequeue(out _)){}
    }
    public void Dispose()
    {
        if(manager!=null)manager.CustomMessagingManager?.UnregisterNamedMessageHandler(Message);
        ResetStreams();manager=null;peers.Clear();modes.Clear();hostWorld="";GuestWorld="";lastMode=false;epoch=0;nextHello=nextOffer=0;
        foreach(var hidden in remoteRenderers.Values)hidden.Restore();remoteRenderers.Clear();
    }
}
