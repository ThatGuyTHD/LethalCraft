using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace LethalCraft.Network;

// Turns one loopback TCP stream into bounded, acknowledged chunks. The game thread only
// queues/drains frames; socket reads and writes never block Unity's simulation.
internal sealed class RelayStream : IDisposable
{
    public const int Window=32;
    readonly TcpClient socket;
    readonly NetworkStream network;
    readonly Action<RelayFrame> send;
    readonly ConcurrentQueue<RelayFrame> incoming=new();
    readonly SemaphoreSlim receiveReady=new(0), credit=new(Window);
    readonly CancellationTokenSource stop=new();
    readonly TaskCompletionSource<bool> opened=new(TaskCreationOptions.RunContinuationsAsynchronously);
    int disposed, queued;
    uint outgoing=1, received, acknowledged;
    bool remoteEnded, localEnded, endReceived;
    public bool Closed=>Volatile.Read(ref disposed)!=0;
    public int PendingReceive=>Volatile.Read(ref queued);
    public int InFlight=>Window-credit.CurrentCount;
    public RelayStream(TcpClient socket,Action<RelayFrame> send,bool waitForOpen=false)
    {
        this.socket=socket;this.send=send;socket.NoDelay=true;network=socket.GetStream();
        if(!waitForOpen)opened.TrySetResult(true);
        _=ReadLoop();_=WriteLoop();
    }
    async Task ReadLoop()
    {
        try
        {
            await opened.Task.ConfigureAwait(false);
            var buffer=new byte[RelayFrame.MaxData];
            while(!Closed)
            {
                await credit.WaitAsync(stop.Token).ConfigureAwait(false);
                int count=await network.ReadAsync(buffer,0,buffer.Length,stop.Token).ConfigureAwait(false);
                if(count==0){credit.Release();localEnded=true;send(new RelayFrame{Kind=RelayKind.End});if(remoteEnded)Dispose();return;}
                var data=new byte[count];Buffer.BlockCopy(buffer,0,data,0,count);
                send(new RelayFrame{Kind=RelayKind.Data,Sequence=outgoing++,Data=data});
            }
        }
        catch(Exception e) when(e is IOException||e is SocketException||e is ObjectDisposedException||e is OperationCanceledException){Abort();}
    }
    async Task WriteLoop()
    {
        try
        {
            while(!Closed)
            {
                await receiveReady.WaitAsync(stop.Token).ConfigureAwait(false);
                if(!incoming.TryDequeue(out var frame))continue;
                if(frame.Kind==RelayKind.End)
                {
                    socket.Client.Shutdown(SocketShutdown.Send);remoteEnded=true;if(localEnded)Dispose();return;
                }
                await network.WriteAsync(frame.Data,0,frame.Data.Length,stop.Token).ConfigureAwait(false);
                Interlocked.Decrement(ref queued);
                send(new RelayFrame{Kind=RelayKind.Ack,Sequence=frame.Sequence});
            }
        }
        catch(Exception e) when(e is IOException||e is SocketException||e is ObjectDisposedException||e is OperationCanceledException){Abort();}
    }
    public void Receive(RelayFrame frame)
    {
        if(Closed)return;
        switch(frame.Kind)
        {
            case RelayKind.Opened: opened.TrySetResult(true);break;
            case RelayKind.Data:
                if(endReceived||frame.Data.Length==0||frame.Data.Length>RelayFrame.MaxData||frame.Sequence!=received+1||Interlocked.Increment(ref queued)>Window){Abort();return;}
                received=frame.Sequence;incoming.Enqueue(frame);receiveReady.Release();break;
            case RelayKind.Ack:
                if(frame.Sequence!=acknowledged+1||frame.Sequence>=outgoing){Abort();return;}
                acknowledged=frame.Sequence;credit.Release();break;
            case RelayKind.End:
                if(!endReceived){endReceived=true;incoming.Enqueue(frame);receiveReady.Release();}break;
            case RelayKind.Abort: Dispose();break;
        }
    }
    void Abort(){if(Closed)return;send(new RelayFrame{Kind=RelayKind.Abort});Dispose();}
    public void Dispose()
    {
        if(Interlocked.Exchange(ref disposed,1)!=0)return;
        stop.Cancel();opened.TrySetCanceled();socket.Dispose();
        while(incoming.TryDequeue(out _)){}
    }
}
