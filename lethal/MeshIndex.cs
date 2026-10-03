using System;
using System.Collections.Generic;
using UnityEngine;

namespace LethalCraft;

/// <summary>Immutable local-space BVH, built off the game thread and shared by mesh instances.</summary>
internal sealed class MeshIndex
{
    internal readonly Vector3[] Vertices;
    internal readonly int[] Indices;
    readonly int[] order;
    readonly List<Node> nodes=new();
    readonly struct Node
    {
        public readonly Vector3 Min,Max;
        public readonly int Start,Count,Left,Right;
        public Node(Vector3 min,Vector3 max,int start,int count,int left,int right)
            =>(Min,Max,Start,Count,Left,Right)=(min,max,start,count,left,right);
    }
    public int TriangleCount=>order.Length;
    public MeshIndex(Vector3[] vertices,int[] indices)
    {
        Vertices=vertices;Indices=indices;order=new int[indices.Length/3];
        for(int i=0;i<order.Length;i++)order[i]=i*3;
        if(order.Length>0)Build(0,order.Length);
    }
    int Build(int start,int count)
    {
        Vector3 min=new(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity),max=-min;
        for(int i=start;i<start+count;i++)for(int k=0;k<3;k++)
        {var v=Vertices[Indices[order[i]+k]];min=Vector3.Min(min,v);max=Vector3.Max(max,v);}
        int id=nodes.Count;nodes.Add(default);
        if(count<=24){nodes[id]=new Node(min,max,start,count,-1,-1);return id;}
        var size=max-min;int axis=size.x>=size.y&&size.x>=size.z?0:size.y>=size.z?1:2;
        Array.Sort(order,start,count,Comparer<int>.Create((a,b)=>Centroid(a,axis).CompareTo(Centroid(b,axis))));
        int left=Build(start,count/2),right=Build(start+count/2,count-count/2);
        nodes[id]=new Node(min,max,start,0,left,right);return id;
    }
    float Centroid(int tri,int axis)=>(Vertices[Indices[tri]][axis]+Vertices[Indices[tri+1]][axis]+Vertices[Indices[tri+2]][axis])/3;
    public void Query(Vector3 min,Vector3 max,List<int> result)
    {result.Clear();if(nodes.Count>0)QueryNode(0,min,max,result);}
    void QueryNode(int id,Vector3 min,Vector3 max,List<int> result)
    {
        var n=nodes[id];
        if(n.Min.x>max.x||n.Max.x<min.x||n.Min.y>max.y||n.Max.y<min.y||n.Min.z>max.z||n.Max.z<min.z)return;
        if(n.Count>0){for(int i=n.Start;i<n.Start+n.Count;i++)result.Add(order[i]);return;}
        QueryNode(n.Left,min,max,result);QueryNode(n.Right,min,max,result);
    }
}
