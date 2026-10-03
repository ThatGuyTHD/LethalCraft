using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using LethalCraft.Network;

static class RelayChecks
{
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
    static async Task<(TcpClient app,TcpClient relay)> Pair()
    {
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        var app=new TcpClient();var connect=app.ConnectAsync(IPAddress.Loopback,((IPEndPoint)listener.LocalEndpoint).Port);
        var relay=await listener.AcceptTcpClientAsync();await connect;listener.Stop();return(app,relay);
    }
    static async Task<byte[]> Read(TcpClient client,int length,CancellationToken cancel)
    {
        byte[] result=new byte[length];await client.GetStream().ReadExactlyAsync(result,cancel);return result;
    }
    public static async Task Run()
    {
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var frame=new RelayFrame{Kind=RelayKind.Data,Epoch=123,Stream=45,Sequence=6,Data=new byte[]{1,2,255}};
        var decoded=RelayFrame.Decode(frame.Encode());
        Check(decoded.Epoch==123&&decoded.Stream==45&&decoded.Sequence==6&&decoded.Data.SequenceEqual(frame.Data),"relay frame preserves stream, epoch and binary payload");
        bool rejected=false;try{RelayFrame.Decode(frame.Encode().Concat(new byte[1]).ToArray());}catch(InvalidDataException){rejected=true;}
        Check(rejected,"relay rejects inconsistent payload lengths");
        var a=await Pair();var b=await Pair();using var appA=a.app;using var appB=b.app;
        var aToB=new ConcurrentQueue<RelayFrame>();var bToA=new ConcurrentQueue<RelayFrame>();
        using var ra=new RelayStream(a.relay,aToB.Enqueue,true);using var rb=new RelayStream(b.relay,bToA.Enqueue);
        byte[] request=new byte[4*1024*1024+17],response=new byte[3*1024*1024+97];new Random(42).NextBytes(request);new Random(23).NextBytes(response);
        var writeA=appA.GetStream().WriteAsync(request,timeout.Token).AsTask();
        await Task.Delay(50,timeout.Token);Check(aToB.IsEmpty,"TCP data waits until remote socket Opened acknowledgement");
        ra.Receive(new RelayFrame{Kind=RelayKind.Opened});
        int maxFlight=0;var pump=Task.Run(async()=>{
            while(!timeout.IsCancellationRequested&&(!ra.Closed||!rb.Closed||!aToB.IsEmpty||!bToA.IsEmpty))
            {
                if(aToB.TryDequeue(out var f))rb.Receive(RelayFrame.Decode(f.Encode()));
                if(bToA.TryDequeue(out f))ra.Receive(RelayFrame.Decode(f.Encode()));
                maxFlight=Math.Max(maxFlight,Math.Max(ra.InFlight,rb.InFlight));
                await Task.Delay(1,timeout.Token);
            }
        },timeout.Token);
        var receivedB=Read(appB,request.Length,timeout.Token);
        var receivedA=Read(appA,response.Length,timeout.Token);
        var writeB=appB.GetStream().WriteAsync(response,timeout.Token).AsTask();
        await Task.WhenAll(writeA,writeB,receivedA,receivedB).WaitAsync(timeout.Token);
        Check((await receivedA).SequenceEqual(response)&&(await receivedB).SequenceEqual(request),"simultaneous multi-megabyte transfers arrive intact across fragmented relay");
        Check(maxFlight<=RelayStream.Window&&maxFlight>0,"relay backpressure stays within the fixed memory window");
        var streamA=appA.GetStream();var streamB=appB.GetStream();
        appA.Client.Shutdown(SocketShutdown.Send);appB.Client.Shutdown(SocketShutdown.Send);
        byte[] eof=new byte[1];Check(await streamA.ReadAsync(eof,timeout.Token)==0&&await streamB.ReadAsync(eof,timeout.Token)==0,"half-close drains both directions and delivers TCP EOF");
        await pump.WaitAsync(timeout.Token);
        var c=await Pair();using var appC=c.app;var frames=new ConcurrentQueue<RelayFrame>();using var rc=new RelayStream(c.relay,frames.Enqueue);
        rc.Receive(new RelayFrame{Kind=RelayKind.Data,Sequence=2,Data=new byte[]{1}});
        Check(rc.Closed&&frames.Any(f=>f.Kind==RelayKind.Abort),"out-of-order data closes only the offending stream");
        Console.WriteLine("7 relay checks passed.");
    }
}
