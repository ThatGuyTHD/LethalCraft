using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;

namespace LethalCraft.Bridge;

public sealed unsafe class SharedLink : IDisposable
{
    readonly MemoryMappedFile mapping;
    readonly MemoryMappedViewAccessor view;
    readonly Mutex owner;
    byte* memory;
    int overlayFront = 2;
    bool disposed;
    [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
    public static ulong Clock => GetTickCount64();
    public ulong PeerHeartbeat => Read64(0x18);
    public bool Connected { get { ulong beat = PeerHeartbeat, now = Clock; return beat != 0 && now >= beat && now - beat < 3000; } }
    public uint PeerPid => Read32(0x0C);

    public SharedLink(string name = Protocol.MappingName)
    {
        owner = new Mutex(true, name + "_host", out bool first);
        if (!first) { owner.Dispose(); throw new IOException("Another LethalCraft host already owns this bridge."); }
        try
        {
            mapping = MemoryMappedFile.CreateOrOpen(name, Protocol.MappingBytes, MemoryMappedFileAccess.ReadWrite);
            view = mapping.CreateViewAccessor(0, Protocol.MappingBytes, MemoryMappedFileAccess.ReadWrite);
            byte* address = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref address);
            memory = address + view.PointerOffset;
            // Publish heartbeat last. A surviving MC process cannot consume partial initialization.
            Write64(0x10, 0);
            foreach (long offset in new[] { 0L, Protocol.HostState, Protocol.McState, Protocol.OverlayControl,
                Protocol.Input, Protocol.Actors, Protocol.Events, Protocol.Entities, Protocol.Collision, Protocol.Render })
                for (int i = 0; i < 0x100; i++) memory[offset + i] = 0;
            Write32(0, Protocol.Magic); Write32(4, Protocol.Version);
            Write32(8, (uint)Process.GetCurrentProcess().Id);
            Heartbeat();
        }
        catch { owner.ReleaseMutex(); owner.Dispose(); throw; }
    }

    public void Heartbeat() => Write64(0x10, Clock);
    public uint Read32(long p) => unchecked((uint)Volatile.Read(ref *(int*)(memory + p)));
    public ulong Read64(long p) => unchecked((ulong)Volatile.Read(ref *(long*)(memory + p)));
    public void Write32(long p, uint value) => Volatile.Write(ref *(int*)(memory + p), unchecked((int)value));
    public void Write64(long p, ulong value) => Volatile.Write(ref *(long*)(memory + p), unchecked((long)value));
    public void Float(long p, float value) => *(float*)(memory + p) = value;
    public void Double(long p, double value) => *(double*)(memory + p) = value;

    public void PublishHost(uint flags, uint world, uint epoch, double x, double y, double z,
        float yaw, float pitch, uint teleport, int width, int height, float hour = 12,
        uint session = 0, uint saveRequest = 0, uint saveFlags = 0, string worldName = "", uint networkRole = 0, uint connectPort = 0, int moonId = -1, int moonCount = 0)
    {
        if(worldName.Length>=96)throw new ArgumentOutOfRangeException(nameof(worldName));
        foreach(char c in worldName)if(!(c>='a'&&c<='z'||c>='A'&&c<='Z'||c>='0'&&c<='9'||c=='_'||c=='-'))throw new ArgumentException("Invalid world name",nameof(worldName));
        long b = Protocol.HostState;
        uint seq = Read32(b) & ~1u;
        Write32(b, seq + 1); Thread.MemoryBarrier();
        Write32(b + 4, flags); Write32(b + 8, world); Write32(b + 12, epoch);
        Double(b + 16, x); Double(b + 24, y); Double(b + 32, z);
        Float(b + 40, yaw); Float(b + 44, pitch); Write32(b + 48, teleport);
        Write32(b + 52, (uint)Math.Max(1, Math.Min(width, Protocol.MaxWidth)));
        Write32(b + 56, (uint)Math.Max(1, Math.Min(height, Protocol.MaxHeight)));
        Float(b + 60, hour);
        Write32(b + 0x40,session);Write32(b + 0x44,saveRequest);Write32(b + 0x48,saveFlags);
        for(int i=0;i<96;i++)memory[b+0x50+i]=i<worldName.Length?(byte)worldName[i]:(byte)0;
        Write32(b+0xB0,networkRole);Write32(b+0xB4,connectPort);
        Write32(b+0xB8,unchecked((uint)moonId));Write32(b+0xBC,unchecked((uint)moonCount));
        Thread.MemoryBarrier(); Write32(b, seq + 2);
    }

    public bool TryPlayer(out PlayerState s)
    {
        s = default;
        if (!Connected) return false;
        long b = Protocol.McState;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            uint seq = Read32(b);
            if (seq == 0 || (seq & 1) != 0) continue;
            s.Flags = Read32(b + 4); s.X = *(double*)(memory + b + 8);
            s.Y = *(double*)(memory + b + 16); s.Z = *(double*)(memory + b + 24);
            s.Yaw = *(float*)(memory + b + 32); s.Pitch = *(float*)(memory + b + 36);
            s.EyeHeight = *(float*)(memory + b + 40); s.Sensitivity = *(float*)(memory + b + 44);
            s.TeleportAck = Read32(b + 48); s.Frame = Read64(b + 56);
            s.Fov = *(float*)(memory + b + 64); s.BobPhase = *(float*)(memory + b + 68);
            s.BobAmount = *(float*)(memory + b + 72);
            s.EyeX = *(double*)(memory + b + 80); s.EyeY = *(double*)(memory + b + 88); s.EyeZ = *(double*)(memory + b + 96);
            s.CameraMode = Read32(b + 192); s.CameraDistance = *(float*)(memory + b + 196);
            s.Health = *(float*)(memory + b + 200); s.MaxHealth = *(float*)(memory + b + 204);
            s.SessionAck=Read32(b+0xD0);s.SaveAck=Read32(b+0xD4);s.SessionError=Read32(b+0xD8);
            s.ServerPort=Read32(b+0xE0);
            s.MoonAck=Read32(b+0xE4);
            Thread.MemoryBarrier();
            if (seq == Read32(b)) return s.Valid;
        }
        return false;
    }

    public bool Input(ushort type, ushort code = 0, int a = 0, int b = 0, int c = 0)
    {
        long r = Protocol.Input;
        ulong head = Read64(r), tail = Read64(r + 0x40);
        if (head < tail || head - tail >= 4096) return false;
        long p = r + 0x80 + (long)(head % 4096) * 16;
        *(ushort*)(memory + p) = type; *(ushort*)(memory + p + 2) = code;
        *(int*)(memory + p + 4) = a; *(int*)(memory + p + 8) = b; *(int*)(memory + p + 12) = c;
        Thread.MemoryBarrier(); Write64(r, head + 1); return true;
    }

    public bool CollisionMessage(uint type, byte[] payload) => WriteRing(Protocol.Collision, Protocol.CollisionBytes, type, payload);
    public bool WriteRing(long ring, long bytes, uint type, byte[] payload)
    {
        long capacity = bytes - 0x80, required = Protocol.Align8(8L + payload.Length);
        if (required > capacity - 8 || type == 0) throw new ArgumentOutOfRangeException(nameof(payload));
        ulong head = Read64(ring), tail = Read64(ring + 0x40);
        long pos = (long)(head % (ulong)capacity), padding = pos + required > capacity ? capacity - pos : 0;
        if (head < tail || head - tail > (ulong)capacity || (ulong)(required + padding) > (ulong)capacity - (head - tail)) return false;
        if (padding != 0)
        {
            Write32(ring + 0x80 + pos, 0); Write32(ring + 0x84 + pos, 0);
            head += (ulong)padding; pos = 0;
        }
        long start = ring + 0x80 + pos;
        Write32(start, type); Write32(start + 4, (uint)payload.Length);
        Marshal.Copy(payload, 0, (IntPtr)(memory + start + 8), payload.Length);
        Thread.MemoryBarrier(); Write64(ring, head + (ulong)required); return true;
    }

    public int ReadRender(Action<uint, byte[]> accept, int budget = 24)
    {
        long ring = Protocol.Render, capacity = Protocol.RenderBytes - 0x80;
        ulong head = Read64(ring), tail = Read64(ring + 0x40);
        if (head < tail || head - tail > (ulong)capacity) throw new InvalidDataException("Invalid render ring counters.");
        int count = 0;
        while (tail < head && count < budget)
        {
            long pos = (long)(tail % (ulong)capacity), start = ring + 0x80 + pos;
            uint type = Read32(start), length = Read32(start + 4);
            if (type == 0) { tail += (ulong)(capacity - pos); Write64(ring + 0x40, tail); continue; }
            long size = Protocol.Align8(8L + length);
            if (length > capacity - 8 || size > capacity - pos || (ulong)size > head - tail)
                throw new InvalidDataException("Invalid render message length.");
            var payload = new byte[length];
            Marshal.Copy((IntPtr)(memory + start + 8), payload, 0, payload.Length);
            tail += (ulong)size; Write64(ring + 0x40, tail); count++;
            accept(type, payload);
        }
        return count;
    }

    public void ReadEvents(Action<GameEvent> accept)
    {
        long r = Protocol.Events;
        ulong head = Read64(r), tail = Read64(r + 0x40);
        if (head < tail || head - tail > 512) throw new InvalidDataException("Invalid event ring counters.");
        while (tail < head)
        {
            long p = r + 0x80 + (long)(tail % 512) * 32;
            var e = new GameEvent(Read32(p), Read32(p + 4), *(float*)(memory + p + 8), *(float*)(memory + p + 12),
                *(float*)(memory + p + 16), *(float*)(memory + p + 20), Read32(p + 24), Read32(p + 28));
            tail++; Write64(r + 0x40, tail); accept(e);
        }
    }

    public bool TryOverlay(out IntPtr pixels, out int width, out int height, out bool bottomUp)
    {
        pixels = IntPtr.Zero; width = height = 0; bottomUp = false;
        if ((Read32(Protocol.OverlayControl) & 4) == 0) return false;
        int state = Interlocked.Exchange(ref *(int*)(memory + Protocol.OverlayControl), overlayFront);
        overlayFront = state & 3;
        if (overlayFront > 2) throw new InvalidDataException("Invalid overlay slot.");
        long h = Protocol.OverlayHeaders + overlayFront * 0x40;
        width = (int)Read32(h); height = (int)Read32(h + 4); bottomUp = (Read32(h + 8) & 1) != 0;
        if (width < 1 || height < 1 || width > Protocol.MaxWidth || height > Protocol.MaxHeight) return false;
        pixels = (IntPtr)(memory + Protocol.Pixels + overlayFront * (long)Protocol.PixelSlotBytes);
        return true;
    }

    public void PublishActors(byte[] records)
    {
        if (records.Length % 64 != 0 || records.Length > 256 * 64) throw new ArgumentOutOfRangeException(nameof(records));
        long r = Protocol.Actors; uint seq = Read32(r) & ~1u;
        Write32(r, seq + 1); Thread.MemoryBarrier();
        Marshal.Copy(records, 0, (IntPtr)(memory + r + 0x40), records.Length);
        Write32(r + 4, (uint)(records.Length / 64)); Thread.MemoryBarrier(); Write32(r, seq + 2);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Write64(0x10, 0);
        view.SafeMemoryMappedViewHandle.ReleasePointer(); view.Dispose(); mapping.Dispose();
        owner.ReleaseMutex(); owner.Dispose(); memory = null;
    }
}
