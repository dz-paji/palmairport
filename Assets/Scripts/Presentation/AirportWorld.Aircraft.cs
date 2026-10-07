using System.Collections.Generic;
using UnityEngine;

namespace IslandAirport
{
    public static partial class AirportWorld
    {
        // Shared construction primitives only. Each airframe has its own silhouette and layout.
        static readonly Dictionary<string, Mesh> AircraftMeshes = new Dictionary<string, Mesh>();
        const int AircraftSides = 40;

        static GameObject AircraftMeshObject(Transform parent,string name,Mesh mesh,Vector3 position,Color color,AirportStyle.Finish finish=AirportStyle.Finish.Plastic)
        {
            var item=new GameObject(name); item.transform.SetParent(parent,false); item.transform.localPosition=position;
            item.AddComponent<MeshFilter>().sharedMesh=mesh;
            item.AddComponent<MeshRenderer>().sharedMaterial=AirportStyle.SharedMaterial(color,finish);
            return item;
        }

        static Mesh AircraftFinishMesh(string key,List<Vector3> vertices,List<int> triangles)
        {
            var mesh=new Mesh { name="Chubby Aircraft " + key };
            if(vertices.Count>65535)mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);mesh.SetTriangles(triangles,0);mesh.RecalculateNormals();
            // Weld normals at rotational seams and poles without welding UV coordinates.
            var sums=new Dictionary<Vector3,Vector3>();var n=mesh.normals;
            for(int i=0;i<vertices.Count;i++)
            {
                Vector3 p=QuantizedAircraftPoint(vertices[i]);
                Vector3 sum;sums.TryGetValue(p,out sum);sums[p]=sum+n[i];
            }
            for(int i=0;i<n.Length;i++)n[i]=sums[QuantizedAircraftPoint(vertices[i])].normalized;
            mesh.normals=n;
            var uv=new Vector2[vertices.Count];
            for(int i=0;i<uv.Length;i++)uv[i]=new Vector2(vertices[i].x/5+.5f,Mathf.Atan2(vertices[i].z,vertices[i].y)/(Mathf.PI*2)+.5f);
            mesh.uv=uv;mesh.RecalculateBounds();AircraftMeshes[key]=mesh;return mesh;
        }
        static Vector3 QuantizedAircraftPoint(Vector3 p)
        { return new Vector3(Mathf.Round(p.x*100000),Mathf.Round(p.y*100000),Mathf.Round(p.z*100000)); }

        static void AircraftTriangle(List<Vector3> v,List<int> t,int a,int b,int c,Vector3 outward)
        {
            Vector3 normal=Vector3.Cross(v[b]-v[a],v[c]-v[a]);
            if(normal.sqrMagnitude<1e-15f)return;
            t.Add(a);bool flip=Vector3.Dot(normal,outward)<0;t.Add(flip?c:b);t.Add(flip?b:c);
        }
        static GameObject AircraftLathe(Transform parent,string name,string key,Vector2[] profile,Vector3 position,Color color,AirportStyle.Finish finish=AirportStyle.Finish.Plastic)
        {
            Mesh mesh;
            if(!AircraftMeshes.TryGetValue(key,out mesh)||!mesh)
            {
                var v=new List<Vector3>();var t=new List<int>();
                for(int r=0;r<profile.Length;r++)for(int i=0;i<=AircraftSides;i++)
                {float a=i*Mathf.PI*2/AircraftSides;v.Add(new Vector3(profile[r].x,Mathf.Cos(a)*profile[r].y,Mathf.Sin(a)*profile[r].y));}
                for(int r=0;r<profile.Length-1;r++)for(int i=0;i<AircraftSides;i++)
                {
                    int a=r*(AircraftSides+1)+i,b=a+AircraftSides+1;
                    // Profile may reverse along the inner inlet: preserve its winding.
                    Vector2 d=profile[r+1]-profile[r];float angle=(i+.5f)*Mathf.PI*2/AircraftSides;
                    Vector3 outward=new Vector3(-d.y,Mathf.Cos(angle)*d.x,Mathf.Sin(angle)*d.x);
                    AircraftTriangle(v,t,a,a+1,b,outward);AircraftTriangle(v,t,a+1,b+1,b,outward);
                }
                mesh=AircraftFinishMesh(key,v,t);
            }
            return AircraftMeshObject(parent,name,mesh,position,color,finish);
        }

        static void AircraftPanel(Transform parent,string name,string key,Vector2[] outline,float height,float thickness,bool vertical,int side,Color color)
        {
            Mesh mesh;if(!AircraftMeshes.TryGetValue(key,out mesh)||!mesh)
            {
                var points=new List<Vector2>(outline);
                // Subdivide the silhouette twice; no sharp polygon corners remain.
                for(int pass=0;pass<2;pass++)
                {
                    var next=new List<Vector2>();for(int i=0;i<points.Count;i++)
                    {Vector2 a=points[i],b=points[(i+1)%points.Count];next.Add(Vector2.Lerp(a,b,.2f));next.Add(Vector2.Lerp(a,b,.8f));}
                    points=next;
                }
                Vector2 center=Vector2.zero;foreach(var p in points)center+=p;center/=points.Count;
                var v=new List<Vector3>();var t=new List<int>();int count=points.Count;
                float[] scales={.86f,.93f,.985f,1,.985f,.93f,.86f};
                float[] levels={-.5f,-.46f,-.28f,0,.28f,.46f,.5f};
                for(int r=0;r<scales.Length;r++)foreach(var point in points)
                {
                    Vector2 p=Vector2.Lerp(center,point,scales[r]);float h=height+levels[r]*thickness;
                    v.Add(vertical?new Vector3(p.x,p.y,h):new Vector3(p.x,h,p.y*side));
                }
                Vector3 inside=vertical?new Vector3(center.x,center.y,height):new Vector3(center.x,height,center.y*side);
                for(int r=0;r<scales.Length-1;r++)for(int i=0;i<count;i++)
                {
                    int a=r*count+i,b=r*count+(i+1)%count,c=b+count,d=a+count;
                    Vector3 outward=(v[a]+v[b]+v[c]+v[d])*.25f-inside;
                    AircraftTriangle(v,t,a,b,c,outward);AircraftTriangle(v,t,a,c,d,outward);
                }
                for(int end=0;end<2;end++)
                {
                    int ring=end==0?0:(scales.Length-1)*count,start=v.Count;Vector3 p=inside;
                    if(vertical)p.z+=(end==0?-.5f:.5f)*thickness;else p.y+=(end==0?-.5f:.5f)*thickness;
                    v.Add(p);for(int i=0;i<count;i++)AircraftTriangle(v,t,start,ring+i,ring+(i+1)%count,p-inside);
                }
                mesh=AircraftFinishMesh(key,v,t);
            }
            AircraftMeshObject(parent,name,mesh,Vector3.zero,color);
        }

    }
}
