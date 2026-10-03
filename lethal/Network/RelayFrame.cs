using System;
using System.IO;

namespace LethalCraft.Network;

internal enum RelayKind : byte { Hello=1, Offer=2, Open=3, Data=4, Ack=5, End=6, Abort=7, Mode=8, Modes=9, Flash=10, Opened=11 }

internal sealed class RelayFrame
{
    public const int Version=1, MaxData=8192, Header=24;
    public RelayKind Kind;
    public uint Epoch, Stream, Sequence;
    public byte[] Data=Array.Empty<byte>();
    public byte[] Encode()
    {
        if(Data.Length>MaxData)throw new InvalidDataException("Relay payload too large");
        using var buffer=new MemoryStream(Header+Data.Length);using var writer=new BinaryWriter(buffer);
        writer.Write(0x4C435246u);writer.Write((byte)Version);writer.Write((byte)Kind);writer.Write((ushort)0);
        writer.Write(Epoch);writer.Write(Stream);writer.Write(Sequence);writer.Write(Data.Length);writer.Write(Data);
        return buffer.ToArray();
    }
    public static RelayFrame Decode(byte[] bytes)
    {
        if(bytes.Length<Header||bytes.Length>Header+MaxData)throw new InvalidDataException("Invalid relay size");
        using var buffer=new MemoryStream(bytes,false);using var reader=new BinaryReader(buffer);
        if(reader.ReadUInt32()!=0x4C435246u||reader.ReadByte()!=Version)throw new InvalidDataException("LethalCraft multiplayer versions differ");
        var kind=(RelayKind)reader.ReadByte();reader.ReadUInt16();
        if(kind<RelayKind.Hello||kind>RelayKind.Opened)throw new InvalidDataException("Unknown relay message");
        var frame=new RelayFrame{Kind=kind,Epoch=reader.ReadUInt32(),Stream=reader.ReadUInt32(),Sequence=reader.ReadUInt32()};
        int count=reader.ReadInt32();if(count<0||count!=bytes.Length-Header)throw new InvalidDataException("Invalid relay payload length");
        frame.Data=reader.ReadBytes(count);return frame;
    }
}
