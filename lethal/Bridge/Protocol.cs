using System;

namespace LethalCraft.Bridge;

// SkyCraft's MIT-licensed layout, extended with health and per-save world handshakes.
// The matching Java constants are in fabric/.../link/Proto.java.
public static class Protocol
{
    public const string MappingName = "Local\\LethalCraft_v1";
    public const uint Magic = 0x43594B53, Version = 16;
    public const long HostState = 0x100, McState = 0x200, OverlayControl = 0x300, OverlayHeaders = 0x340;
    public const long Input = 0x1000, Actors = 0x12000, Events = 0x17000, Entities = 0x1C000, Collision = 0x20000;
    public const long CollisionBytes = 32L << 20;
    public const long Pixels = Collision + CollisionBytes;
    public const int MaxWidth = 3840, MaxHeight = 2160, PixelSlotBytes = MaxWidth * MaxHeight * 4;
    public const long Render = Pixels + PixelSlotBytes * 3L, RenderBytes = 64L << 20;
    public const long MappingBytes = Render + RenderBytes;
    public static long Align8(long n) => checked((n + 7) & ~7L);
    public static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
}

public struct PlayerState
{
    public uint Flags, TeleportAck, SessionAck, SaveAck, SessionError, ServerPort, MoonAck;
    public double X, Y, Z, EyeX, EyeY, EyeZ;
    public float Yaw, Pitch, EyeHeight, Sensitivity, Fov, BobPhase, BobAmount, CameraDistance, Health, MaxHealth;
    public uint CameraMode;
    public ulong Frame;
    public bool InWorld => (Flags & 1) != 0;
    public bool MenuOpen => (Flags & 2) != 0;
    public bool Dead => (Flags & 32) != 0;
    public bool Valid => Protocol.Finite(X) && Protocol.Finite(Y) && Protocol.Finite(Z)
        && Math.Abs(X) < 30_000_000 && Math.Abs(Z) < 30_000_000 && Math.Abs(Y) < 4096
        && Protocol.Finite(Yaw) && Protocol.Finite(Pitch) && Protocol.Finite(EyeHeight)
        && Protocol.Finite(Health) && Protocol.Finite(MaxHealth);
}

public readonly struct GameEvent
{
    public readonly uint Type, Actor, Flags, Weapon;
    public readonly float A, B, C, D;
    public GameEvent(uint type, uint actor, float a, float b, float c, float d, uint flags, uint weapon)
        => (Type, Actor, A, B, C, D, Flags, Weapon) = (type, actor, a, b, c, d, flags, weapon);
}
