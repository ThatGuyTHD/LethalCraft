using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;
using System.Threading.Tasks;
using LethalCraft.Bridge;

static class Checks
{
    static int passed;
    static void Check(bool condition,string message){if(!condition)throw new Exception(message);Console.WriteLine("PASS "+message);passed++;}
    static void Main()
    {
        string name="Local\\LethalCraft_test_"+Guid.NewGuid().ToString("N");
        using var host=new SharedLink(name);
        using var peer=MemoryMappedFile.OpenExisting(name);
        using var data=peer.CreateViewAccessor();
        Check(data.ReadUInt32(0)==Protocol.Magic&&data.ReadUInt32(4)==15,"v15 handshake and allocated layout");
        host.PublishHost(1,7,1,0,0,0,0,0,1,1280,720,session:12,saveRequest:5,saveFlags:1,worldName:"LethalCraft_A_123",networkRole:2,connectPort:23456);
        Check(data.ReadUInt32(Protocol.HostState+0xB0)==2&&data.ReadUInt32(Protocol.HostState+0xB4)==23456,"guest role and loopback port travel atomically with save identity");
        Check(data.ReadUInt32(Protocol.HostState+0x40)==12&&data.ReadUInt32(Protocol.HostState+0x44)==5&&data.ReadByte(Protocol.HostState+0x50)=='L',"world identity and save requests travel inside host snapshot");
        host.PublishHost(0,0,1,0,0,0,0,0,1,1280,720,session:13);
        Check(data.ReadUInt32(Protocol.HostState+0x40)==13&&data.ReadByte(Protocol.HostState+0x50)==0&&data.ReadByte(Protocol.HostState+0x5F)==0,"main menu clears the complete previous world name");
        bool unsafeName=false;try{host.PublishHost(0,0,1,0,0,0,0,0,1,1280,720,worldName:"../other");}catch(ArgumentException){unsafeName=true;}
        Check(unsafeName&&(data.ReadUInt32(Protocol.HostState)&1)==0,"unsafe world names fail before taking the seqlock");
        for(int i=0;i<4096;i++)if(!host.Input(1,26,i))throw new Exception("early full ring");
        Check(!host.Input(1,26,4096),"full input queue rejects writes without overwriting unread input");
        Check(data.ReadInt32(Protocol.Input+0x80+4095*16+4)==4095,"last input remains intact");
        data.Write(Protocol.Input+0x40,4096UL);
        Check(host.Input(1,26,777)&&data.ReadInt32(Protocol.Input+0x80+4)==777,"input wraps and resumes after consumption");

        long cap=Protocol.RenderBytes-0x80;
        data.Write(Protocol.Render,(ulong)(cap-16));data.Write(Protocol.Render+0x40,(ulong)(cap-16));
        byte[] sample={1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17};
        Check(host.WriteRing(Protocol.Render,Protocol.RenderBytes,7,sample),"variable-size frame wraps with explicit padding");
        int received=host.ReadRender((type,bytes)=>{if(type!=7||!bytes.AsSpan().SequenceEqual(sample))throw new Exception("torn render payload");});
        Check(received==1&&data.ReadUInt64(Protocol.Render)==data.ReadUInt64(Protocol.Render+0x40),"consumer skips padding and receives exact bytes");

        data.Write(0x0C,123u);data.Write(0x18,SharedLink.Clock);
        data.Write(Protocol.McState,1u);data.Write(Protocol.McState+4,1u);
        Check(!host.TryPlayer(out _),"seqlock refuses a player snapshot while the writer owns it");
        data.Write(Protocol.McState+8,12.5);data.Write(Protocol.McState+16,7.0);data.Write(Protocol.McState+24,-3.5);
        data.Write(Protocol.McState+200,13.0f);data.Write(Protocol.McState+204,20.0f);
        data.Write(Protocol.McState+0xD0,12u);data.Write(Protocol.McState+0xD4,5u);data.Write(Protocol.McState+0xD8,0u);data.Write(Protocol.McState+0xE0,23457u);data.Write(Protocol.McState,2u);
        Check(host.TryPlayer(out var state)&&state.X==12.5&&state.Health==13&&state.MaxHealth==20,"extended health and position cross-process layout");
        Check(state.SessionAck==12&&state.SaveAck==5&&state.SessionError==0,"Minecraft reports exact loaded session and completed save");
        Check(state.ServerPort==23457,"published Minecraft endpoint crosses bridge");
        data.Write(Protocol.McState+8,double.NaN);
        Check(!host.TryPlayer(out _),"non-finite player positions never reach the game controller");

        data.Write(Protocol.Render,8UL);data.Write(Protocol.Render+0x40,0UL);
        data.Write(Protocol.Render+0x80,2u);data.Write(Protocol.Render+0x84,0xffffffffu);
        bool rejected=false;try{host.ReadRender((_,_)=>{});}catch(InvalidDataException){rejected=true;}
        Check(rejected,"invalid render lengths are rejected before copying shared memory");

        bool done=false;Exception? error=null;
        var producer=Task.Run(()=>{try{for(uint i=1;i<=20000;i++)host.PublishHost(1,i,i,i,i*2.0,i*3.0,0,0,i,1280,720);}catch(Exception e){error=e;}finally{Volatile.Write(ref done,true);}});
        int snapshots=0;
        while(!Volatile.Read(ref done))
        {
            uint first=data.ReadUInt32(Protocol.HostState);if(first==0||(first&1)!=0)continue;
            uint world=data.ReadUInt32(Protocol.HostState+8);double x=data.ReadDouble(Protocol.HostState+16),y=data.ReadDouble(Protocol.HostState+24),z=data.ReadDouble(Protocol.HostState+32);
            Thread.MemoryBarrier();uint last=data.ReadUInt32(Protocol.HostState);
            if(first!=last)continue;
            if(x!=world||y!=world*2.0||z!=world*3.0)throw new Exception("accepted torn host snapshot");snapshots++;
        }
        producer.GetAwaiter().GetResult();if(error!=null)throw error;
        Check(snapshots>0,"concurrent seqlock reader sees only complete host frames");
        data.Write(0x18,0UL);Check(!host.Connected,"heartbeat loss disconnects the bridge");
        Console.WriteLine($"{passed} bridge checks passed.");
        RelayChecks.Run().GetAwaiter().GetResult();
    }
}
