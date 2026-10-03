using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LethalCraft.Bridge;
using UnityEngine;
using UnityEngine.Rendering;

namespace LethalCraft;

public static class Coordinates
{
    // A reflection makes Unity's positive yaw match Minecraft's positive yaw.
    public static Vector3 ToMinecraft(Vector3 p) => new(-p.x, p.y, p.z);
    public static Vector3 ToUnity(double x, double y, double z) => new((float)-x, (float)y, (float)z);
    public static Vector3 ToUnity(Vector3 p) => new(-p.x, p.y, p.z);
}

internal readonly struct Triangle
{
    public readonly Vector3 A, B, C;
    public Triangle(Vector3 a, Vector3 b, Vector3 c) => (A, B, C) = (a, b, c);
    public bool Touches(Vector3 lo, Vector3 hi)
    {
        // Separating-axis triangle / box intersection. Used for conservative sub-voxel coverage.
        Vector3 centre = (lo + hi) * .5f, ext = (hi - lo) * .5f;
        Vector3 a = A - centre, b = B - centre, c = C - centre;
        if (Math.Min(a.x, Math.Min(b.x, c.x)) > ext.x || Math.Max(a.x, Math.Max(b.x, c.x)) < -ext.x
            || Math.Min(a.y, Math.Min(b.y, c.y)) > ext.y || Math.Max(a.y, Math.Max(b.y, c.y)) < -ext.y
            || Math.Min(a.z, Math.Min(b.z, c.z)) > ext.z || Math.Max(a.z, Math.Max(b.z, c.z)) < -ext.z) return false;
        Vector3 e0 = b - a, e1 = c - b, e2 = a - c;
        if (Separates(Vector3.Cross(e0, e1), a, b, c, ext)) return false;
        if (EdgeSeparates(e0,a,b,c,ext)||EdgeSeparates(e1,a,b,c,ext)||EdgeSeparates(e2,a,b,c,ext)) return false;
        return true;
    }
    static bool EdgeSeparates(Vector3 e,Vector3 a,Vector3 b,Vector3 c,Vector3 ext)
        => Separates(new Vector3(0,e.z,-e.y),a,b,c,ext)
            || Separates(new Vector3(-e.z,0,e.x),a,b,c,ext)
            || Separates(new Vector3(e.y,-e.x,0),a,b,c,ext);
    static bool Separates(Vector3 axis, Vector3 a, Vector3 b, Vector3 c, Vector3 ext)
    {
        float r = Math.Abs(axis.x) * ext.x + Math.Abs(axis.y) * ext.y + Math.Abs(axis.z) * ext.z + 1e-6f;
        float pa = Vector3.Dot(a, axis), pb = Vector3.Dot(b, axis), pc = Vector3.Dot(c, axis);
        return Math.Min(pa, Math.Min(pb, pc)) > r || Math.Max(pa, Math.Max(pb, pc)) < -r;
    }
}

internal sealed class WorldCollision : IDisposable
{
    readonly SharedLink link;
    readonly Dictionary<int, Task<MeshIndex>> meshes = new();
    readonly Dictionary<int, MeshReadback> readbacks=new();
    readonly List<int> candidates=new();
    Collider[] overlaps=new Collider[128];
    readonly HashSet<int> unreadable = new();
    readonly Dictionary<Vector3Int, float> sent = new();
    readonly Queue<(uint type, byte[] bytes)> pending = new();
    Task<(byte[] triangles, byte[] voxels)>? job;
    uint jobEpoch, epoch = 1;
    public int RegionsSent { get; private set; }
    public int UnsupportedMeshes => unreadable.Count;
    public double LastCaptureMilliseconds { get; private set; }
    public double MaxCaptureMilliseconds { get; private set; }
    public long CandidateTriangles { get; private set; }
    public long SourceTriangles { get; private set; }
    readonly Dictionary<Vector3Int,int> fingerprints=new();
    float nextCapture;
    public WorldCollision(SharedLink link) => this.link = link;

    public void Reset(uint newEpoch)
    {
        epoch = newEpoch; sent.Clear();fingerprints.Clear(); pending.Clear(); RegionsSent = 0;
        meshes.Clear();unreadable.Clear();MaxCaptureMilliseconds=0;CandidateTriangles=0;SourceTriangles=0;
        pending.Enqueue((1, BitConverter.GetBytes(epoch)));
    }

    public void Tick(Vector3 player)
    {
        if (job != null && job.IsCompleted)
        {
            if (job.IsFaulted) Plugin.Log.LogError(job.Exception);
            else if (jobEpoch == epoch)
            {
                pending.Enqueue((3, job.Result.triangles)); pending.Enqueue((2, job.Result.voxels)); RegionsSent++;
            }
            job = null;
        }
        while (pending.Count > 0)
        {
            var message = pending.Peek();
            if (!link.CollisionMessage(message.type, message.bytes)) return;
            pending.Dequeue();
        }
        if (job != null||Time.unscaledTime<nextCapture) return;
        Vector3 p = Coordinates.ToMinecraft(player);
        var centre = new Vector3Int(Mathf.FloorToInt(p.x / 8), Mathf.FloorToInt(p.y / 8), Mathf.FloorToInt(p.z / 8));
        Vector3Int selected = default; float priority = float.MinValue;
        for (int x = -2; x <= 2; x++) for (int y = -2; y <= 1; y++) for (int z = -2; z <= 2; z++)
        {
            var region = centre + new Vector3Int(x, y, z);
            float distance = x * x + y * y + z * z;
            float age = sent.TryGetValue(region, out float stamp) ? Time.unscaledTime - stamp : 1000;
            if (age < (distance <= 3 ? .35f : 5f)) continue;
            float score = age >= 1000 ? 2000 - distance * 100 : age * 10 - distance * 2;
            if (score > priority) { priority = score; selected = region; }
        }
        if (priority == float.MinValue) return;
        var origin = selected * 8;
        var timer=System.Diagnostics.Stopwatch.StartNew();
        List<Triangle> triangles = Capture(origin, origin + new Vector3Int(8, 8, 8),out bool complete);
        LastCaptureMilliseconds=timer.Elapsed.TotalMilliseconds;MaxCaptureMilliseconds=Math.Max(MaxCaptureMilliseconds,LastCaptureMilliseconds);
        nextCapture=Time.unscaledTime+.025f;
        if(!complete)return;
        sent[selected] = Time.unscaledTime; jobEpoch = epoch;
        // Static regions do not need repeated voxelization, copies, or a Minecraft collision rebuild.
        int hash=triangles.Count;
        unchecked{foreach(var t in triangles){hash=hash*31+t.A.GetHashCode();hash=hash*31+t.B.GetHashCode();hash=hash*31+t.C.GetHashCode();}}
        if(fingerprints.TryGetValue(selected,out int previous)&&hash==previous)return;
        fingerprints[selected]=hash;
        uint capturedEpoch = epoch;
        job = Task.Run(() => Encode(origin, triangles, capturedEpoch));
    }

    List<Triangle> Capture(Vector3 lo, Vector3 hi,out bool complete)
    {
        var result = new List<Triangle>();complete=true;
        Vector3 middle = Coordinates.ToUnity((lo + hi) * .5f);
        int count;
        while((count=Physics.OverlapBoxNonAlloc(middle,Vector3.one*4.05f,overlaps,Quaternion.identity,~0,QueryTriggerInteraction.Ignore))==overlaps.Length)
            Array.Resize(ref overlaps,overlaps.Length*2);
        for(int c=0;c<count;c++)
        {
            Collider collider=overlaps[c];
            if (!collider.enabled || collider is CharacterController || collider.GetComponentInParent<BridgeObject>()
                || collider.GetComponentInParent<GameNetcodeStuff.PlayerControllerB>() || collider.GetComponentInParent<EnemyAI>()) continue;
            if (Plugin.LocalPlayer != null && Physics.GetIgnoreLayerCollision(Plugin.LocalPlayer.gameObject.layer, collider.gameObject.layer)) continue;
            if (collider is MeshCollider mc && mc.sharedMesh != null)
            {
                if (!TryMesh(mc.sharedMesh, out var data)){if(!unreadable.Contains(mc.sharedMesh.GetInstanceID()))complete=false;continue;}
                Matrix4x4 matrix=collider.transform.localToWorldMatrix,inverse=collider.transform.worldToLocalMatrix;
                Vector3 localMin=new(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity),localMax=-localMin;
                for(int corner=0;corner<8;corner++)
                {
                    Vector3 v=inverse.MultiplyPoint3x4(Coordinates.ToUnity(new Vector3((corner&1)==0?lo.x:hi.x,(corner&2)==0?lo.y:hi.y,(corner&4)==0?lo.z:hi.z)));
                    localMin=Vector3.Min(localMin,v);localMax=Vector3.Max(localMax,v);
                }
                data.Query(localMin-Vector3.one*.001f,localMax+Vector3.one*.001f,candidates);
                SourceTriangles+=data.TriangleCount;CandidateTriangles+=candidates.Count;
                foreach(int i in candidates)
                    Add(result,matrix.MultiplyPoint3x4(data.Vertices[data.Indices[i]]),
                        matrix.MultiplyPoint3x4(data.Vertices[data.Indices[i+1]]),matrix.MultiplyPoint3x4(data.Vertices[data.Indices[i+2]]),lo,hi);
            }
            else if (collider is BoxCollider box)
                Box(result, box.transform, box.center, box.size, lo, hi);
            else if (collider is TerrainCollider terrain && terrain.terrainData != null)
                Terrain(result, terrain, middle, lo, hi);
            else if (collider is SphereCollider || collider is CapsuleCollider)
            {
                // These are mostly movable props. A bounded convex proxy keeps them solid.
                Box(result, null, collider.bounds.center, collider.bounds.size, lo, hi);
            }
        }
        return result;
    }

    sealed class MeshReadback : IDisposable
    {
        public GraphicsBuffer Vertices=null!,Indices=null!;
        public AsyncGPUReadbackRequest VertexRequest,IndexRequest;
        public byte[]? VertexBytes,IndexBytes;
        public bool VertexDone,IndexDone,Error;
        public uint Epoch;
        public int Offset,Stride,VertexCount,IndexSize;
        public UnityEngine.Rendering.SubMeshDescriptor[] Submeshes=Array.Empty<UnityEngine.Rendering.SubMeshDescriptor>();
        public void Dispose(){Vertices?.Dispose();Indices?.Dispose();}
    }
    bool TryMesh(Mesh mesh, out MeshIndex data)
    {
        int id = mesh.GetInstanceID();data=null!;
        if (meshes.TryGetValue(id, out var build))
        {
            if(!build.IsCompleted)return false;
            if(build.IsFaulted){unreadable.Add(id);return false;}
            data=build.Result;return true;
        }
        if (unreadable.Contains(id)) return false;
        try
        {
            if (mesh.isReadable)
            {var vertices=mesh.vertices;var indices=mesh.triangles;meshes[id]=Task.Run(()=>new MeshIndex(vertices,indices));return false;}
            else
            {
                if (mesh.GetVertexAttributeFormat(VertexAttribute.Position) != VertexAttributeFormat.Float32)
                    throw new NotSupportedException("Non-float position stream");
                if(!readbacks.TryGetValue(id,out var readback))
                {
                    int stream=mesh.GetVertexAttributeStream(VertexAttribute.Position);
                    readback=new MeshReadback{Epoch=epoch,Vertices=mesh.GetVertexBuffer(stream),Indices=mesh.GetIndexBuffer(),
                        Offset=mesh.GetVertexAttributeOffset(VertexAttribute.Position),Stride=mesh.GetVertexBufferStride(stream),
                        VertexCount=mesh.vertexCount,IndexSize=mesh.indexFormat==IndexFormat.UInt16?2:4,
                        Submeshes=new UnityEngine.Rendering.SubMeshDescriptor[mesh.subMeshCount]};
                    for(int s=0;s<mesh.subMeshCount;s++)readback.Submeshes[s]=mesh.GetSubMesh(s);
                    readbacks[id]=readback;
                    var capture=readback;
                    readback.VertexRequest=AsyncGPUReadback.Request(readback.Vertices,request=>
                    {
                        capture.Error|=request.hasError;
                        if(!request.hasError)capture.VertexBytes=request.GetData<byte>().ToArray();
                        capture.VertexDone=true;CompleteReadback(id,capture);
                    });
                    readback.IndexRequest=AsyncGPUReadback.Request(readback.Indices,request=>
                    {
                        capture.Error|=request.hasError;
                        if(!request.hasError)capture.IndexBytes=request.GetData<byte>().ToArray();
                        capture.IndexDone=true;CompleteReadback(id,capture);
                    });
                    return false;
                }
                return false;
            }
        }
        catch (Exception e)
        {
            if(readbacks.TryGetValue(id,out var failed)){readbacks.Remove(id);failed.Dispose();}
            unreadable.Add(id); Plugin.Log.LogWarning($"Collision mesh '{mesh.name}' unavailable: {e.Message}"); return false;
        }
    }
    void CompleteReadback(int id,MeshReadback capture)
    {
        if(!capture.VertexDone||!capture.IndexDone)return;
        readbacks.Remove(id);capture.Dispose();
        if(capture.Epoch!=epoch)return;
        if(capture.Error){unreadable.Add(id);Plugin.Log.LogWarning("Collision GPU mesh readback failed");return;}
        // A request's native data expires after completion. Copy in its callback, not on a later region visit.
        meshes[id]=Task.Run(()=>DecodeMesh(capture,capture.VertexBytes!,capture.IndexBytes!));
    }
    static MeshIndex DecodeMesh(MeshReadback readback,byte[] vb,byte[] ib)
    {
                var vertices = new Vector3[readback.VertexCount];
                for (int i = 0; i < vertices.Length; i++)
                {
                    int p = i * readback.Stride + readback.Offset;
                    vertices[i] = new Vector3(BitConverter.ToSingle(vb, p), BitConverter.ToSingle(vb, p + 4), BitConverter.ToSingle(vb, p + 8));
                }
                var indices = new List<int>(); int indexSize = readback.IndexSize;
                foreach(var sub in readback.Submeshes)
                {
                    if (sub.topology != MeshTopology.Triangles) continue;
                    for (int i = 0; i < sub.indexCount; i++)
                    {
                        int p = (sub.indexStart + i) * indexSize;
                        indices.Add(sub.baseVertex + (indexSize == 2 ? BitConverter.ToUInt16(ib, p) : BitConverter.ToInt32(ib, p)));
                    }
                }
                return new MeshIndex(vertices,indices.ToArray());
    }
    public void Dispose(){foreach(var readback in readbacks.Values)readback.Dispose();readbacks.Clear();}

    static void Add(List<Triangle> list, Vector3 a, Vector3 b, Vector3 c, Vector3 lo, Vector3 hi)
    {
        var tri = new Triangle(Coordinates.ToMinecraft(a), Coordinates.ToMinecraft(c), Coordinates.ToMinecraft(b));
        if (Vector3.Cross(tri.B - tri.A, tri.C - tri.A).sqrMagnitude > 1e-12f && tri.Touches(lo, hi)) list.Add(tri);
    }
    static readonly int[] BoxIndices = { 0,2,1, 0,3,2, 4,5,6, 4,6,7, 0,1,5, 0,5,4, 3,7,6, 3,6,2, 0,4,7, 0,7,3, 1,2,6, 1,6,5 };
    static void Box(List<Triangle> list, Transform? transform, Vector3 centre, Vector3 size, Vector3 lo, Vector3 hi)
    {
        Vector3 e = size * .5f;
        var v = new[] { new Vector3(-e.x,-e.y,-e.z), new Vector3(e.x,-e.y,-e.z), new Vector3(e.x,e.y,-e.z), new Vector3(-e.x,e.y,-e.z),
            new Vector3(-e.x,-e.y,e.z), new Vector3(e.x,-e.y,e.z), new Vector3(e.x,e.y,e.z), new Vector3(-e.x,e.y,e.z) };
        for (int i = 0; i < 8; i++) v[i] = transform != null ? transform.TransformPoint(centre + v[i]) : centre + v[i];
        for (int i = 0; i < BoxIndices.Length; i += 3) Add(list, v[BoxIndices[i]], v[BoxIndices[i+1]], v[BoxIndices[i+2]], lo, hi);
    }
    static void Terrain(List<Triangle> list, TerrainCollider terrain, Vector3 middle, Vector3 lo, Vector3 hi)
    {
        TerrainData td = terrain.terrainData; Vector3 origin = terrain.transform.position;
        int x0 = Mathf.FloorToInt(middle.x - 5 - origin.x), z0 = Mathf.FloorToInt(middle.z - 5 - origin.z);
        Vector3 Point(int x, int z) => origin + new Vector3(x, td.GetInterpolatedHeight(x / td.size.x, z / td.size.z), z);
        for (int x = x0; x < x0 + 10; x++) for (int z = z0; z < z0 + 10; z++)
        {
            if (x < 0 || z < 0 || x + 1 > td.size.x || z + 1 > td.size.z) continue;
            var a = Point(x,z); var b = Point(x,z+1); var c = Point(x+1,z+1); var d = Point(x+1,z);
            Add(list,a,b,c,lo,hi); Add(list,a,c,d,lo,hi);
        }
    }

    static (byte[], byte[]) Encode(Vector3Int origin, List<Triangle> triangles, uint epoch)
    {
        using var tm = new MemoryStream(); using var tw = new BinaryWriter(tm);
        Header(tw, origin, epoch, triangles.Count);
        var masks = new Dictionary<Vector3Int, ulong[]>();
        foreach (Triangle tri in triangles)
        {
            foreach (Vector3 v in new[] { tri.A, tri.B, tri.C }) { tw.Write(v.x); tw.Write(v.y); tw.Write(v.z); }
            tw.Write(0u); // Host geometry is collision; terrain destruction is not enabled.
            Vector3 min = Vector3.Max(origin, Vector3.Min(tri.A, Vector3.Min(tri.B, tri.C)) - Vector3.one * .001f);
            Vector3 max = Vector3.Min(origin + new Vector3(8,8,8), Vector3.Max(tri.A, Vector3.Max(tri.B, tri.C)) + Vector3.one * .001f);
            for (int x = Math.Max(origin.x*4, (int)Math.Floor(min.x*4)); x < Math.Min((origin.x+8)*4, (int)Math.Floor(max.x*4)+1); x++)
            for (int y = Math.Max(origin.y*4, (int)Math.Floor(min.y*4)); y < Math.Min((origin.y+8)*4, (int)Math.Floor(max.y*4)+1); y++)
            for (int z = Math.Max(origin.z*4, (int)Math.Floor(min.z*4)); z < Math.Min((origin.z+8)*4, (int)Math.Floor(max.z*4)+1); z++)
            {
                var lo = new Vector3(x*.25f,y*.25f,z*.25f);
                if (!tri.Touches(lo,lo+Vector3.one*.25f)) continue;
                var block = new Vector3Int((int)Math.Floor(x/4.0),(int)Math.Floor(y/4.0),(int)Math.Floor(z/4.0));
                if (!masks.TryGetValue(block,out ulong[] bits)) masks[block] = bits = new ulong[8];
                int sx=(x-block.x*4)*2, sy=(y-block.y*4)*2, sz=(z-block.z*4)*2;
                ulong mask = 3UL << (sz*8+sx) | 3UL << ((sz+1)*8+sx);
                bits[sy] |= mask; bits[sy+1] |= mask;
            }
        }
        using var vm = new MemoryStream(); using var vw = new BinaryWriter(vm);
        Header(vw,origin,epoch,masks.Count);
        foreach (var pair in masks)
        {
            vw.Write(pair.Key.x); vw.Write(pair.Key.y); vw.Write(pair.Key.z); vw.Write(0);
            foreach (ulong word in pair.Value) vw.Write(word);
        }
        return (tm.ToArray(), vm.ToArray());
    }
    static void Header(BinaryWriter w, Vector3Int p, uint epoch, int count)
    {
        w.Write(p.x);w.Write(p.y);w.Write(p.z);w.Write(p.x+7);w.Write(p.y+7);w.Write(p.z+7);w.Write(epoch);w.Write(count);
    }
}

internal sealed class BridgeObject : MonoBehaviour { }
