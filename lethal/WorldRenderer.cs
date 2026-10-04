using System;
using System.Collections.Generic;
using System.IO;
using LethalCraft.Bridge;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace LethalCraft;

internal sealed class WorldRenderer : IDisposable
{
    readonly GameObject root;
    readonly Dictionary<Vector3Int, GameObject> sections = new(), solids = new(), lights = new();
    readonly Dictionary<uint, Texture2D> textures = new();
    readonly Dictionary<(uint, uint, uint), Material> materials = new();
    readonly GameObject avatar, scene;
    readonly MeshRenderer avatarRenderer;
    Texture2D? overlay;
    bool overlayBottomUp;
    bool atlasDirty;
    byte[]? nextAvatar,nextScene;
    public int SectionCount => sections.Count;
    public int TriangleCount { get; private set; }
    public bool HasOverlay => overlay != null;
    internal bool LocalAvatarVisible => avatar.activeInHierarchy && avatarRenderer.enabled
        && !avatarRenderer.forceRenderingOff && avatar.GetComponent<MeshFilter>().sharedMesh.vertexCount>0;
    internal Vector3 LocalAvatarPosition => avatar.transform.position;
    public WorldRenderer()
    {
        root = new GameObject("LethalCraft world") { hideFlags = HideFlags.HideAndDontSave }; root.AddComponent<BridgeObject>(); Object.DontDestroyOnLoad(root);
        avatar = NewMesh("Minecraft player"); scene = NewMesh("Minecraft entities");
        avatarRenderer=avatar.GetComponent<MeshRenderer>();avatarRenderer.forceRenderingOff=true;
    }
    GameObject NewMesh(string name)
    {
        var go = new GameObject(name); go.transform.SetParent(root.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = new Mesh { indexFormat = IndexFormat.UInt32, name = name };
        var renderer = go.AddComponent<MeshRenderer>(); renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
        return go;
    }
    public void Visible(bool visible) => root.SetActive(visible);
    // Mesh packets may activate the object again; presentation must survive those updates.
    public void ShowLocalAvatar(bool visible) => avatarRenderer.forceRenderingOff=!visible;
    public void PositionAvatar(PlayerState state) => avatar.transform.position = Coordinates.ToUnity(state.X,state.Y,state.Z);

    public void Receive(uint type, byte[] payload)
    {
        using var stream = new MemoryStream(payload, false); using var r = new BinaryReader(stream);
        switch (type)
        {
            case 1: Texture(0, r.ReadInt32(), r.ReadInt32(), r); break;
            case 2:
            {
                Vector3Int key = Key(r); int count = r.ReadInt32();
                if (count == 0) { Remove(sections,key); break; }
                if (!sections.TryGetValue(key,out GameObject go)) sections[key] = go = NewMesh("MC section " + key);
                go.transform.position = Coordinates.ToUnity(key * 16);
                SetMesh(go,r,count,new[] { new Batch(0,0,count,0) }); TriangleCount += count / 3;
                break;
            }
            case 3: Clear(); break;
            case 4:
            {
                uint id=r.ReadUInt32();int w=r.ReadInt32(),h=r.ReadInt32();r.ReadUInt32(); Texture(id,w,h,r);break;
            }
            case 5: nextAvatar=payload; break;
            case 6: nextScene=payload; break;
            case 7:
            {
                int x=r.ReadInt32(),y=r.ReadInt32(),w=r.ReadInt32(),h=r.ReadInt32();
                if (!textures.TryGetValue(0,out Texture2D atlas)) break;
                if(x<0||y<0||w<1||h<1||x+w>atlas.width||y+h>atlas.height) throw new InvalidDataException("Atlas update outside texture");
                byte[] rgba=r.ReadBytes(checked(w*h*4));if(rgba.Length!=w*h*4)throw new EndOfStreamException();
                var colours=new Color32[w*h];for(int i=0;i<colours.Length;i++)colours[i]=new Color32(rgba[i*4],rgba[i*4+1],rgba[i*4+2],rgba[i*4+3]);
                atlas.SetPixels32(x,y,w,h,colours);atlasDirty=true;break;
            }
            case 8: Lights(r);break;
            case 10: Solids(r);break;
            // Ragdolls and host-world digging are separate integrations, not interpreted as blocks.
        }
    }
    public void Flush()
    {
        // Animation regions share one atlas: upload it once, not once for every animated tile.
        if(atlasDirty&&textures.TryGetValue(0,out Texture2D atlas)){atlas.Apply(false,false);atlasDirty=false;}
        if(nextAvatar!=null){using var r=new BinaryReader(new MemoryStream(nextAvatar,false));Model(avatar,r,false);nextAvatar=null;}
        if(nextScene!=null){using var r=new BinaryReader(new MemoryStream(nextScene,false));Model(scene,r,true);nextScene=null;}
    }
    internal BoxCollider[] BlockColliders()=>root.GetComponentsInChildren<BoxCollider>();
    internal string Diagnostic()=> $"active={root.activeInHierarchy}, sections={sections.Count}, solids={solids.Count}, colliders={BlockColliders().Length}, atlasDirty={atlasDirty}";
    void Texture(uint id,int w,int h,BinaryReader r)
    {
        if(w<1||h<1||w>8192||h>8192||(long)w*h*4>r.BaseStream.Length-r.BaseStream.Position)throw new InvalidDataException("Invalid texture");
        if(!textures.TryGetValue(id,out Texture2D texture)||texture.width!=w||texture.height!=h)
        {
            if(texture!=null)Object.Destroy(texture);
            textures[id]=texture=new Texture2D(w,h,TextureFormat.RGBA32,false,false){name="MC texture "+id,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            foreach(var pair in materials)if(pair.Key.Item1==id)SetTexture(pair.Value,texture);
        }
        texture.LoadRawTextureData(r.ReadBytes(checked(w*h*4)));texture.Apply(false,false);
    }
    static void SetTexture(Material m,Texture2D t)
    {
        if(m.HasProperty("_BaseColorMap"))m.SetTexture("_BaseColorMap",t);
        if(m.HasProperty("_MainTex"))m.SetTexture("_MainTex",t);
        if(m.HasProperty("_BaseMap"))m.SetTexture("_BaseMap",t);
    }
    Material Material(uint texture,uint flags,uint tint)
    {
        var key=(texture,flags&3,tint);
        if(materials.TryGetValue(key,out Material m))return m;
        Shader shader=Shader.Find("HDRP/Lit")??Shader.Find("Standard");
        if(shader==null)throw new InvalidOperationException("No supported world shader");
        m=new Material(shader){name="LethalCraft material"};materials[key]=m;
        Color color=new Color32((byte)tint,(byte)(tint>>8),(byte)(tint>>16),255);
        if(m.HasProperty("_BaseColor"))m.SetColor("_BaseColor",color);
        if(m.HasProperty("_Color"))m.SetColor("_Color",color);
        if(m.HasProperty("_Metallic"))m.SetFloat("_Metallic",0);
        if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",.05f);
        if(m.HasProperty("_DoubleSidedEnable"))m.SetFloat("_DoubleSidedEnable",1);
        if(m.HasProperty("_CullMode"))m.SetFloat("_CullMode",0);
        if(m.HasProperty("_CullModeForward"))m.SetFloat("_CullModeForward",0);
        if((flags&1)!=0)
        {
            m.EnableKeyword("_ALPHATEST_ON");m.SetFloat("_AlphaCutoffEnable",1);m.SetFloat("_AlphaCutoff",.1f);
        }
        if((flags&2)!=0)
        {
            m.SetFloat("_SurfaceType",1);m.SetFloat("_BlendMode",0);m.SetFloat("_SrcBlend",5);m.SetFloat("_DstBlend",10);
            m.SetFloat("_ZWrite",0);m.SetFloat("_TransparentZWrite",0);m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");m.renderQueue=3000;
        }
        // Let HDRP set its matching stencil, depth and blend keywords for a runtime material.
        UnityEngine.Rendering.HighDefinition.HDMaterial.ValidateMaterial(m);
        if(textures.TryGetValue(texture,out Texture2D t))SetTexture(m,t);
        return m;
    }
    readonly struct Batch
    {
        public readonly uint Texture,Flags;public readonly int First,Count;
        public Batch(uint texture,int first,int count,uint flags)=>(Texture,First,Count,Flags)=(texture,first,count,flags);
    }
    void Model(GameObject go,BinaryReader r,bool world)
    {
        if(world)go.transform.position=Coordinates.ToUnity(r.ReadDouble(),r.ReadDouble(),r.ReadDouble());
        int batches=r.ReadInt32(),vertices=r.ReadInt32();
        if(batches<0||batches>4096)throw new InvalidDataException("Invalid batch count");
        var b=new Batch[batches];for(int i=0;i<batches;i++)b[i]=new Batch(r.ReadUInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadUInt32());
        go.SetActive(vertices>0);if(vertices>0)SetMesh(go,r,vertices,b);
    }
    void SetMesh(GameObject go,BinaryReader r,int count,Batch[] batches)
    {
        if(count<0||count>2_000_000||count%3!=0||(long)count*32>r.BaseStream.Length-r.BaseStream.Position)throw new InvalidDataException("Invalid mesh vertices");
        var positions=new Vector3[count];var uv=new Vector2[count];var colours=new uint[count];var flags=new uint[count];
        for(int i=0;i<count;i++)
        {
            positions[i]=new Vector3(-r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
            uv[i]=new Vector2(r.ReadSingle(),r.ReadSingle());colours[i]=r.ReadUInt32();r.ReadUInt32();flags[i]=r.ReadUInt32();
        }
        var groups=new Dictionary<Material,List<int>>();
        foreach(Batch batch in batches)
        {
            if(batch.First<0||batch.Count<0||batch.First+batch.Count>count||batch.Count%3!=0)throw new InvalidDataException("Invalid mesh batch");
            for(int i=batch.First;i<batch.First+batch.Count;i+=3)
            {
                uint color=Average(colours[i],colours[i+1],colours[i+2]);
                uint mode=flags[i]&3;if((batch.Flags&1)!=0)mode|=2;
                Material material=Material(batch.Texture,mode,color);
                if(!groups.TryGetValue(material,out List<int> indices))groups[material]=indices=new List<int>();
                indices.Add(i);indices.Add(i+2);indices.Add(i+1);
            }
        }
        Mesh mesh=go.GetComponent<MeshFilter>().sharedMesh;mesh.Clear();mesh.vertices=positions;mesh.uv=uv;
        mesh.subMeshCount=groups.Count;int n=0;var mats=new Material[groups.Count];
        foreach(var pair in groups){mats[n]=pair.Key;mesh.SetTriangles(pair.Value,n++);}
        mesh.RecalculateNormals();mesh.RecalculateBounds();go.GetComponent<MeshRenderer>().sharedMaterials=mats;
    }
    static uint Average(uint a,uint b,uint c)
    {
        uint result=0xff000000;
        for(int shift=0;shift<24;shift+=8)
        {
            uint mean=(((a>>shift)&255)+((b>>shift)&255)+((c>>shift)&255))/3;
            mean=Math.Min(255,((mean+8)/16)*16);result|=mean<<shift;
        }
        return result;
    }
    static Vector3Int Key(BinaryReader r)=>new(r.ReadInt32(),r.ReadInt32(),r.ReadInt32());
    void Solids(BinaryReader r)
    {
        var key=Key(r);int count=r.ReadInt32();Remove(solids,key);if(count==0)return;
        if(count<0||count>4096)throw new InvalidDataException("Invalid solid count");
        byte[] bits=r.ReadBytes(512);if(bits.Length!=512)throw new EndOfStreamException();
        var occupied=new bool[4096];for(int i=0;i<4096;i++)occupied[i]=(bits[i>>3]&(1<<(i&7)))!=0;
        var group=new GameObject("MC collision "+key);group.transform.SetParent(root.transform,false);solids[key]=group;
        int layer=LayerMask.NameToLayer("Room");if(layer<0)layer=0;
        for(int y=0;y<16;y++)for(int z=0;z<16;z++)for(int x=0;x<16;x++)
        {
            int Index(int a,int b,int c)=>a+c*16+b*256;
            if(!occupied[Index(x,y,z)])continue;
            int nx=x+1;while(nx<16&&occupied[Index(nx,y,z)])nx++;
            int nz=z+1;while(nz<16){bool ok=true;for(int a=x;a<nx;a++)if(!occupied[Index(a,y,nz)])ok=false;if(!ok)break;nz++;}
            int ny=y+1;while(ny<16){bool ok=true;for(int c=z;c<nz;c++)for(int a=x;a<nx;a++)if(!occupied[Index(a,ny,c)])ok=false;if(!ok)break;ny++;}
            for(int b=y;b<ny;b++)for(int c=z;c<nz;c++)for(int a=x;a<nx;a++)occupied[Index(a,b,c)]=false;
            var box=new GameObject("Minecraft blocks");box.layer=layer;box.transform.SetParent(group.transform,false);
            box.transform.position=Coordinates.ToUnity((Vector3)(key*16)+new Vector3((x+nx)*.5f,(y+ny)*.5f,(z+nz)*.5f));
            Vector3 size=new(nx-x,ny-y,nz-z);box.AddComponent<BoxCollider>().size=size;
            var obstacle=box.AddComponent<NavMeshObstacle>();obstacle.shape=NavMeshObstacleShape.Box;obstacle.size=size;
            obstacle.carving=true;obstacle.carveOnlyStationary=true;
        }
    }
    void Lights(BinaryReader r)
    {
        var key=Key(r);int count=r.ReadInt32();Remove(lights,key);
        if(count<0||count>4096||count*8L>r.BaseStream.Length-r.BaseStream.Position)throw new InvalidDataException("Invalid light count");
        if(count==0)return;
        var group=new GameObject("MC lights "+key);group.transform.SetParent(root.transform,false);lights[key]=group;
        for(int i=0;i<count;i++)
        {
            Vector3 p=(Vector3)(key*16)+new Vector3(r.ReadByte()+.5f,r.ReadByte()+.5f,r.ReadByte()+.5f);int level=r.ReadByte();uint rgba=r.ReadUInt32();
            if(i>=64)continue;
            var go=new GameObject("Minecraft light");go.transform.SetParent(group.transform,false);go.transform.position=Coordinates.ToUnity(p);
            var light=go.AddComponent<Light>();light.type=LightType.Point;light.range=level*.65f;
            light.color=new Color32((byte)rgba,(byte)(rgba>>8),(byte)(rgba>>16),255);light.shadows=LightShadows.None;
            var hd=go.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalLightData>();hd.SetIntensity(level*35f);
        }
    }
    public void UpdateOverlay(SharedLink link)
    {
        if(!link.TryOverlay(out IntPtr pixels,out int w,out int h,out bool bottomUp))return;
        if(overlay==null||overlay.width!=w||overlay.height!=h)
        {
            if(overlay!=null)Object.Destroy(overlay);
            overlay=new Texture2D(w,h,TextureFormat.RGBA32,false,false){name="Minecraft HUD and hands",filterMode=FilterMode.Bilinear};
        }
        overlay.LoadRawTextureData(pixels,checked(w*h*4));overlay.Apply(false,false);overlayBottomUp=bottomUp;
    }
    public void DrawOverlay()
    {
        if(overlay==null)return;
        GUI.color=Color.white;
        GUI.DrawTextureWithTexCoords(new Rect(0,0,Screen.width,Screen.height),overlay,overlayBottomUp?new Rect(0,0,1,1):new Rect(0,1,1,-1),true);
    }
    static void Remove(Dictionary<Vector3Int,GameObject> objects,Vector3Int key)
    {
        if(objects.TryGetValue(key,out GameObject go)){DestroyMesh(go);Object.Destroy(go);objects.Remove(key);}
    }
    static void DestroyMesh(GameObject go){if(go==null)return;var mf=go.GetComponent<MeshFilter>();if(mf!=null&&mf.sharedMesh!=null)Object.Destroy(mf.sharedMesh);}
    public void Clear()
    {
        nextAvatar=nextScene=null;
        foreach(var dict in new[]{sections,solids,lights}){foreach(GameObject go in dict.Values){DestroyMesh(go);Object.Destroy(go);}dict.Clear();}
        if(avatar!=null)avatar.SetActive(false);if(scene!=null)scene.SetActive(false);
    }
    public void Dispose()
    {
        Clear();DestroyMesh(avatar);DestroyMesh(scene);Object.Destroy(root);
        foreach(Material m in materials.Values)Object.Destroy(m);foreach(Texture2D t in textures.Values)Object.Destroy(t);
        if(overlay!=null)Object.Destroy(overlay);
    }
}
