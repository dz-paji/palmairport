using System.Collections.Generic;
using UnityEngine;

namespace IslandAirport
{
    public static partial class AirportWorld
    {
        static Transform BuildAirframe(string name,AirframeSpec s,AirframePaint paint,Vector3 position)
        {
            var plane=new GameObject(string.IsNullOrEmpty(name)?AircraftConfigurationNames[s.Type]:name).transform;
            plane.position=position;
            AirframeMesh(plane,"Fuselage "+s.Id,"v2-body-"+s.Id,paint.Body,AirportStyle.Finish.Plastic,(v,t)=>
            {
                List<float> xs=AirframeLongitudes(s);
                for(int r=0;r<xs.Count;r++)for(int a=0;a<=AircraftSides;a++)v.Add(AirframePoint(s,xs[r],a*2*Mathf.PI/AircraftSides,0));
                for(int r=0;r<xs.Count-1;r++)for(int a=0;a<AircraftSides;a++)
                {
                    int p=r*(AircraftSides+1)+a,q=p+AircraftSides+1;
                    float theta=(a+.5f)*2*Mathf.PI/AircraftSides;
                    Vector3 normal=new Vector3(0,Mathf.Cos(theta),Mathf.Sin(theta));
                    // At tips, longitudinal normals must point toward the end cap.
                    normal.x=r<4?-1:r>xs.Count-5?1:0;
                    AircraftTriangle(v,t,p,p+1,q,normal);AircraftTriangle(v,t,p+1,q+1,q,normal);
                }
            });
            BuildAirframeLivery(plane,s,paint);
            BuildAirframeWindows(plane,s,paint);
            BuildAirframeWings(plane,s,paint);
            BuildAirframeTail(plane,s,paint);
            BuildAirframeGear(plane,s,paint);
            if(!string.IsNullOrEmpty(name))
            {
                TextMesh label=CreateLabel(plane,name,new Vector3(0,s.BodyY+s.Ry+.20f,0),Window,.0975f);
                label.transform.localRotation=Quaternion.Euler(90,180,0);
            }
            return plane;
        }
        static List<float> AirframeLongitudes(AirframeSpec s)
        {
            var xs=new List<float>();
            for(int i=0;i<=26;i++)xs.Add(Mathf.Lerp(-s.Half,s.TailStart,i/26f));
            for(int i=1;i<=22;i++)xs.Add(Mathf.Lerp(s.TailStart,s.NoseStart,i/22f));
            for(int i=1;i<=26;i++)xs.Add(s.NoseStart+s.NoseLength*Mathf.Sin(i*Mathf.PI/52));
            return xs;
        }
        static float AirframeRadius(AirframeSpec s,float x)
        {
            if(x<=-s.Half||x>=s.Half)return 0;
            if(x<s.TailStart)return Mathf.Pow(Mathf.Sin(Mathf.InverseLerp(-s.Half,s.TailStart,x)*Mathf.PI*.5f),1.12f);
            if(x<=s.NoseStart)return 1+.016f*Mathf.Sin(Mathf.InverseLerp(s.TailStart,s.NoseStart,x)*Mathf.PI);
            float u=(x-s.NoseStart)/s.NoseLength;return Mathf.Sqrt(Mathf.Max(0,1-u*u));
        }
        static float AirframeCenterY(AirframeSpec s,float x)
        {
            float tail=1-Mathf.InverseLerp(-s.Half,s.TailStart,x);
            float nose=Mathf.InverseLerp(s.NoseStart,s.Half,x);
            return s.BodyY+tail*tail*.18f-nose*nose*(s.Type==1?.10f:.065f);
        }
        static Vector3 AirframePoint(AirframeSpec s,float x,float angle,float offset)
        {
            float r=AirframeRadius(s,x);
            return new Vector3(x,AirframeCenterY(s,x)+Mathf.Cos(angle)*(r*s.Ry+offset),Mathf.Sin(angle)*(r*s.Rz+offset));
        }
        static void AirframeMesh(Transform parent,string name,string key,Color color,AirportStyle.Finish finish,System.Action<List<Vector3>,List<int>> build)
        {
            Mesh mesh;
            if(!AircraftMeshes.TryGetValue(key,out mesh)||!mesh)
            {var v=new List<Vector3>();var t=new List<int>();build(v,t);mesh=AircraftFinishMesh(key,v,t);}
            AircraftMeshObject(parent,name,mesh,Vector3.zero,color,finish);
        }
        static void AirframeSurfaceRect(List<Vector3> v,List<int> t,AirframeSpec s,float x0,float x1,float a0,float a1,float offset,int side,int nx=22,int na=8)
        {
            int start=v.Count;
            for(int ix=0;ix<=nx;ix++)for(int ia=0;ia<=na;ia++)v.Add(AirframePoint(s,Mathf.Lerp(x0,x1,ix/(float)nx),Mathf.Lerp(a0,a1,ia/(float)na)*side,offset));
            for(int ix=0;ix<nx;ix++)for(int ia=0;ia<na;ia++)
            {
                int a=start+ix*(na+1)+ia,b=a+na+1;
                Vector3 n=v[a]-new Vector3(v[a].x,AirframeCenterY(s,v[a].x),0);
                AircraftTriangle(v,t,a,b,a+1,n);AircraftTriangle(v,t,a+1,b,b+1,n);
            }
        }
        static void AirframeSurfaceOval(List<Vector3> v,List<int> t,AirframeSpec s,float x,float angle,float width,float height,float offset,int side)
        {
            const int segments=20,rings=3;int start=v.Count;
            for(int r=0;r<=rings;r++)for(int i=0;i<=segments;i++)
            {
                float a=i*Mathf.PI*2/segments,f=r/(float)rings,c=Mathf.Cos(a),sn=Mathf.Sin(a);
                v.Add(AirframePoint(s,x+Mathf.Sign(c)*Mathf.Pow(Mathf.Abs(c),.58f)*width*f,
                    (angle+Mathf.Sign(sn)*Mathf.Pow(Mathf.Abs(sn),.58f)*height*f)*side,offset));
            }
            for(int r=0;r<rings;r++)for(int i=0;i<segments;i++)
            {
                int a=start+r*(segments+1)+i,b=a+segments+1;
                Vector3 n=v[b]-new Vector3(v[b].x,AirframeCenterY(s,v[b].x),0);
                AircraftTriangle(v,t,a,b,b+1,n);AircraftTriangle(v,t,a,b+1,a+1,n);
            }
        }
        static void BuildAirframeLivery(Transform plane,AirframeSpec s,AirframePaint paint)
        {
            AirframeMesh(plane,"Livery continuous underside","v2-belly-"+s.Id+"-"+paint.Index,paint.Belly,AirportStyle.Finish.Plastic,(v,t)=>
            {
                for(int side=-1;side<=1;side+=2)
                {
                    // BA: navy sweeps up at the rear; JAL: narrow light-gray underside;
                    // Cathay: gray lower fuselage; Qatar: white lower hemisphere under silver crown.
                    List<float> xs=AirframeLongitudes(s);
                    int rows=xs.Count-1,angles=24,start=v.Count;
                    for(int r=0;r<=rows;r++)
                    {
                        float x=xs[r];
                        float a=paint.Index==0?Mathf.Lerp(1.60f,2.03f,Mathf.InverseLerp(-s.Half,s.NoseStart,x)):
                            paint.Index==1?2.36f:paint.Index==2?1.96f:1.69f;
                        for(int j=0;j<=angles;j++)v.Add(AirframePoint(s,x,Mathf.Lerp(a,Mathf.PI,j/(float)angles)*side,.017f*AirframeRadius(s,x)));
                    }
                    for(int r=0;r<rows;r++)for(int j=0;j<angles;j++)
                    {
                        int a=start+r*(angles+1)+j,b=a+angles+1;Vector3 n=v[a]-new Vector3(v[a].x,AirframeCenterY(s,v[a].x),0);
                        AircraftTriangle(v,t,a,b,a+1,n);AircraftTriangle(v,t,a+1,b,b+1,n);
                    }
                }
            });
            if(paint.Index==2)
            {
                AirframeMesh(plane,"Cathay-inspired long gray side band","v2-cx-band-"+s.Id,Hex("B8C4C4"),AirportStyle.Finish.Plastic,(v,t)=>
                {
                    for(int side=-1;side<=1;side+=2)
                        AirframeSurfaceRect(v,t,s,-s.Half+.20f,s.Half-.08f,1.43f,1.78f,.017f,side,90,7);
                });
            }
            // The metal wing-root fairing is shared by white/gray liveries, not a logo decal.
        }
        static void BuildAirframeWindows(Transform plane,AirframeSpec s,AirframePaint paint)
        {
            Color glass=Hex("183444");
            for(int side=-1;side<=1;side+=2)
            {
                string suffix=side<0?" Left":" Right";
                for(int row=0;row<(s.Type==1?2:1);row++)
                {
                    float angle=s.Type==1?(row==0?1.57f:.92f):1.26f;
                    float halfWidth=s.Type==1?.073f:s.Type==0?.069f:.069f;
                    float halfAngle=s.Type==1?.105f:.18f;
                    int count=s.Windows-(s.Type==1&&row==1?1:0);
                    for(int rim=0;rim<2;rim++)
                    {
                        bool frame=rim==0;
                        string key="v2-windows-"+s.Id+"-"+row+"-"+side+"-"+rim;
                        AirframeMesh(plane,(frame?"Window frames":"Cabin window row "+row)+suffix,key,frame?paint.Trim:glass,
                            frame?AirportStyle.Finish.Plastic:AirportStyle.Finish.Glass,(v,t)=>
                        {
                            for(int i=0;i<count;i++)
                            {
                                float x=Mathf.Lerp(s.TailStart+.17f,s.NoseStart-.34f,i/(float)(count-1));
                                AirframeSurfaceOval(v,t,s,x,angle,halfWidth+(frame?.022f:0),halfAngle+(frame?.025f:0),frame?.028f:.037f,side);
                            }
                        });
                    }
                }
                // Distinct cockpit placement: A380 sits low between the decks, Comet has a panoramic rounded visor.
                float wind0=s.NoseStart+s.NoseLength*.25f,wind1=s.Half-s.NoseLength*.16f;
                float high=s.Type==1?1.03f:s.Type==2?.78f:.64f,low=s.Type==1?1.60f:1.47f;
                for(int rim=0;rim<2;rim++)
                {
                    bool frame=rim==0;
                    AirframeMesh(plane,(frame?"Cockpit rim":"Cockpit glazing")+suffix,"v2-cockpit-"+s.Id+"-"+side+"-"+rim,
                        frame?paint.Trim:glass,frame?AirportStyle.Finish.Plastic:AirportStyle.Finish.Glass,(v,t)=>
                    {
                        if(frame)AirframeSurfaceRect(v,t,s,wind0-.025f,wind1+.025f,high-.035f,low+.035f,.024f,side,20,12);
                        else
                        {
                            float split=high+(low-high)*.48f;
                            AirframeSurfaceRect(v,t,s,wind0,wind1,high,split-.035f,.034f,side,18,7);
                            AirframeSurfaceRect(v,t,s,wind0,wind1,split+.035f,low,.034f,side,18,7);
                        }
                    });
                }
                int doors=s.Type==1?3:2;
                for(int door=0;door<doors;door++)
                {
                    float x=door==0?s.NoseStart-.12f:door==1?s.TailStart-.13f:-.4f;
                    // Rear doors occupy the tapered end of the cabin rather than covering its last window.
                    for(int rim=0;rim<2;rim++)
                    {
                        bool frame=rim==0;
                        AirframeMesh(plane,"Passenger door "+door+suffix,"v2-door-"+s.Id+"-"+side+"-"+door+"-"+rim,
                            frame?paint.Trim:paint.Body,AirportStyle.Finish.Plastic,(v,t)=>
                            AirframeSurfaceOval(v,t,s,x,s.Type==1?1.48f:1.52f,frame?.109f:.09f,s.Type==1?(frame?.30f:.278f):(frame?.48f:.45f),frame?.041f:.049f,side));
                    }
                }
            }
        }

        static void BuildAirframeWings(Transform plane,AirframeSpec s,AirframePaint paint)
        {
            for(int side=-1;side<=1;side+=2)
            {
                string suffix=side<0?" Left":" Right";
                Vector2[] outline;
                if(s.Type==0)outline=new[] {new Vector2(.82f,.30f),new Vector2(.66f,.83f),new Vector2(-.73f,2.59f),new Vector2(-.88f,2.65f),new Vector2(-1.22f,2.61f),new Vector2(-1.34f,1.76f),new Vector2(-1.42f,.34f)};
                else if(s.Type==1)outline=new[] {new Vector2(1.20f,.43f),new Vector2(1.10f,1.20f),new Vector2(-.18f,2.90f),new Vector2(-1.36f,4.42f),new Vector2(-1.65f,4.45f),new Vector2(-2.05f,3.60f),new Vector2(-2.05f,2.20f),new Vector2(-1.89f,.47f)};
                else outline=new[] {new Vector2(.82f,.28f),new Vector2(.55f,1.03f),new Vector2(-.76f,2.58f),new Vector2(-.97f,2.60f),new Vector2(-1.26f,2.50f),new Vector2(-1.40f,.33f)};
                AircraftPanel(plane,"Swept wing"+suffix,"v2-wing-"+s.Id+side,outline,s.WingY,s.Type==1?.20f:.14f,false,side,Hex("E7E9E4"));
                // A softly raised trailing control surface makes the broad chord readable in top view.
                Vector2[] flap=s.Type==1?new[]{new Vector2(-1.55f,.95f),new Vector2(-1.73f,2.57f),new Vector2(-1.97f,2.70f),new Vector2(-1.84f,.88f)}:
                    s.Type==0?new[]{new Vector2(-1.05f,.85f),new Vector2(-1.12f,1.93f),new Vector2(-1.26f,1.99f),new Vector2(-1.34f,.76f)}:
                    new[]{new Vector2(-1.04f,1.29f),new Vector2(-1.13f,2.17f),new Vector2(-1.24f,2.19f),new Vector2(-1.36f,1.25f)};
                AircraftPanel(plane,"Flap panel"+suffix,"v2-flap-"+s.Id+side,flap,s.WingY+.075f,s.Type==1?.055f:.033f,false,side,Hex("BDC6C8"));
                if(s.Type==0)
                {
                    Vector2[] winglet={new Vector2(-1.23f,s.WingY-.015f),new Vector2(-1.34f,1.78f),new Vector2(-1.21f,1.89f),new Vector2(-.98f,1.78f),new Vector2(-.77f,s.WingY+.08f)};
                    AircraftPanel(plane,"737 blended winglet"+suffix,"v2-winglet-737"+side,winglet,side*2.59f,.075f,true,1,paint.Tail);
                }
                if(s.Type==1)
                {
                    Vector2[] fence={new Vector2(-1.78f,.91f),new Vector2(-1.78f,1.54f),new Vector2(-1.60f,1.61f),new Vector2(-1.39f,1.30f),new Vector2(-1.43f,1.0f)};
                    AircraftPanel(plane,"A380 wingtip fence"+suffix,"v2-fence-380"+side,fence,side*4.39f,.06f,true,1,paint.Body);
                }
                if(s.Type==2)BuildCometWingRoot(plane,s,paint,side);
                else
                {
                    int engines=s.Type==1?2:1;
                    for(int e=0;e<engines;e++)
                    {
                        float z=s.Type==1?(e==0?1.73f:3.12f):1.20f;
                        float x=s.Type==1?(e==0?.70f:-.33f):.51f;
                        float y=s.Type==1?.70f:.66f;
                        float radius=s.Type==1?.40f:.31f,length=s.Type==1?1.40f:1.18f;
                        BuildAirframeEngine(plane,s,paint,new Vector3(x,y,z*side),length,radius,side,e,false);
                        ModelBox(plane,"Engine pylon "+e+suffix,new Vector3(x-.16f,s.WingY-.11f,z*side),new Vector3(length*.50f,.35f,.14f),Hex("D2D8D6"),.035f,Quaternion.identity);
                    }
                }
                AircraftLathe(plane,"Navigation light"+suffix,"v2-nav",new[]{new Vector2(-.05f,0),new Vector2(-.035f,.035f),new Vector2(.035f,.035f),new Vector2(.05f,0)},
                    new Vector3(s.Type==1?-1.57f:s.Type==0?-1.02f:-1.02f,s.WingY+.10f,side*(s.Span*.5f-.06f)),side<0?Hex("ED6157"):Hex("57CCAE"),AirportStyle.Finish.Glow);
            }
        }
        static void BuildCometWingRoot(Transform plane,AirframeSpec s,AirframePaint paint,int side)
        {
            var root=new GameObject(side<0?"Wing Root Left":"Wing Root Right").transform;root.SetParent(plane,false);
            // Thick blended fairing surrounds the two turbojets. No pylons or underwing pods.
            Vector2[] fairing={new Vector2(.84f,.38f),new Vector2(.69f,1.18f),new Vector2(.33f,1.39f),new Vector2(-1.43f,1.27f),new Vector2(-1.58f,.43f)};
            AircraftPanel(root,"Comet blended wing root","v2-comet-fairing"+side,fairing,s.WingY,.34f,false,side,Hex("CCD5D4"));
            for(int e=0;e<2;e++)
            {
                float z=e==0?.70f:1.10f,x=e==0?-.23f:-.37f;
                BuildAirframeEngine(root,s,paint,new Vector3(x,s.WingY,z*side),2.16f,.19f,side,e,true);
            }
            // Comet 4 pinion fuel tank: a closed cigar, intentionally no dark intake.
            AircraftLathe(plane,"Comet pinion fuel tank "+side,"v2-comet-tank",new[]{
                new Vector2(-.56f,0),new Vector2(-.49f,.072f),new Vector2(-.28f,.13f),new Vector2(.15f,.13f),new Vector2(.36f,.105f),new Vector2(.51f,.055f),new Vector2(.56f,0)
            },new Vector3(-.42f,s.WingY+.07f,side*2.03f),Hex("E1E5DF"));
        }
        static void BuildAirframeEngine(Transform parent,AirframeSpec s,AirframePaint paint,Vector3 location,float length,float radius,int side,int index,bool embedded)
        {
            var engine=new GameObject("Engine Assembly "+index+(side<0?" Left":" Right")).transform;
            engine.SetParent(parent,false);engine.localPosition=location;
            float h=length*.5f;string key="v2-engine-"+s.Id+"-"+index;
            Color shell=embedded?Hex("C5CFD0"):paint.Engine;
            var body=AircraftLathe(engine,"Nacelle shell",key+"shell",new[]{
                new Vector2(-h,.52f*radius),new Vector2(-h*.85f,.77f*radius),new Vector2(-h*.49f,.96f*radius),
                new Vector2(h*.46f,radius),new Vector2(h*.82f,radius*.97f),new Vector2(h*.97f,radius*.89f)
            },Vector3.zero,shell);
            Color lip=Hex("D9DDDD");
            AircraftLathe(engine,"Rolled metal intake",key+"lip",new[]{
                new Vector2(h*.82f,radius*.97f),new Vector2(h*.98f,radius*.90f),new Vector2(h*1.04f,radius*.81f),
                new Vector2(h*1.02f,radius*.72f),new Vector2(h*.95f,radius*.68f),new Vector2(h*.77f,radius*.69f)
            },Vector3.zero,lip);
            AircraftLathe(engine,"Deep dark intake",key+"duct",new[]{new Vector2(h*.95f,radius*.68f),new Vector2(h*.50f,radius*.63f),new Vector2(h*.47f,0)},Vector3.zero,Hex("122731"),AirportStyle.Finish.Matte);
            AircraftLathe(engine,"Rear jet exhaust",key+"exhaust",new[]{new Vector2(-h*1.04f,0),new Vector2(-h*1.04f,radius*.48f),new Vector2(-h*.98f,radius*.58f)},Vector3.zero,Hex("30454D"),AirportStyle.Finish.Matte);
            if(embedded)
            {
                // A shallow dark throat in front of the solid wing fairing prevents its
                // structural mesh from visually sealing the integrated turbojet inlets.
                AircraftLathe(engine,"Comet visible intake throat",key+"throat",new[]{
                    new Vector2(h*1.025f,radius*.72f),new Vector2(h*1.025f,0)
                },Vector3.zero,Hex("122731"),AirportStyle.Finish.Matte);
            }
            if(!embedded)
            {
                AirframeMesh(engine,"Turbine blades",key+"fan",Hex("819BA5"),AirportStyle.Finish.Plastic,(v,t)=>
                {
                    for(int i=0;i<12;i++)
                    {
                        float a=i*Mathf.PI/6;int start=v.Count;
                        float[] rr={radius*.18f,radius*.62f,radius*.62f,radius*.18f};float[] aa={a,a+.17f,a+.34f,a+.16f};
                        for(int j=0;j<4;j++)v.Add(new Vector3(h*.57f,Mathf.Cos(aa[j])*rr[j],Mathf.Sin(aa[j])*rr[j]));
                        AircraftTriangle(v,t,start,start+1,start+2,Vector3.right);AircraftTriangle(v,t,start,start+2,start+3,Vector3.right);
                    }
                });
                AircraftLathe(engine,"Fan spinner",key+"spinner",new[]{new Vector2(h*.54f,radius*.20f),new Vector2(h*.67f,radius*.19f),new Vector2(h*.81f,0)},Vector3.zero,Hex("CFD8D9"));
            }
            if(s.Type==0)engine.localScale=new Vector3(1,.84f,1); // distinctive low-clearance 737 intake
        }

        static void BuildAirframeTail(Transform plane,AirframeSpec s,AirframePaint paint)
        {
            float rear=-s.Half+.10f,front=rear+s.FinWidth;
            Vector2[] fin={new Vector2(rear,s.FinBase),new Vector2(rear+.08f,s.TailTop-.07f),new Vector2(rear+.25f,s.TailTop),
                new Vector2(rear+s.FinWidth*.49f,s.TailTop-.02f),new Vector2(front,s.FinBase+.16f),new Vector2(front+.10f,s.FinBase)};
            AircraftPanel(plane,"Vertical tail fin","v2-fin-"+s.Id,fin,0,s.FinThickness,true,1,paint.Tail);
            for(int side=-1;side<=1;side+=2)
            {
                float span=s.Type==1?1.88f:s.Type==0?1.27f:1.06f;
                Vector2[] tail={new Vector2(front-.25f,.03f),new Vector2(rear+.19f,span),new Vector2(rear-.14f,span),new Vector2(rear-.18f,span*.76f),new Vector2(rear+.31f,.04f)};
                AircraftPanel(plane,"Horizontal tailplane "+side,"v2-tailplane-"+s.Id+side,tail,s.FinBase+.05f,.095f,false,side,Hex("E8EBE4"));
                if(paint.Index==0)
                {
                    AirframeTailPatch(plane,s,"White diagonal tail ribbon",0,side,paint.Body,new[]{new Vector2(.08f,.12f),new Vector2(.90f,.49f),new Vector2(.78f,.93f),new Vector2(.08f,.57f)});
                    AirframeTailPatch(plane,s,"Red diagonal tail ribbon",1,side,paint.Accent,new[]{new Vector2(.08f,.21f),new Vector2(.88f,.58f),new Vector2(.82f,.78f),new Vector2(.08f,.40f)});
                }
                else if(paint.Index==1)
                    AirframeTailPatch(plane,s,"Red abstract tail sweep",2,side,paint.Accent,new[]{new Vector2(.16f,.17f),new Vector2(.82f,.36f),new Vector2(.76f,.68f),new Vector2(.56f,.52f),new Vector2(.17f,.36f)});
                else if(paint.Index==3)
                    AirframeTailPatch(plane,s,"Burgundy abstract tail slash",3,side,paint.Accent,new[]{new Vector2(.17f,.17f),new Vector2(.78f,.36f),new Vector2(.73f,.76f),new Vector2(.52f,.58f),new Vector2(.18f,.39f)});
            }
        }
        static void AirframeTailPatch(Transform plane,AirframeSpec s,string name,int patch,int side,Color color,Vector2[] uv)
        {
            AirframeMesh(plane,name+" "+side,"v2-tail-paint-"+s.Id+"-"+patch+"-"+side,color,AirportStyle.Finish.Plastic,(v,t)=>
            {
                float rear=-s.Half+.10f;
                foreach(Vector2 p in uv)
                {
                    float back=rear+.08f*p.y,front=rear+Mathf.Lerp(s.FinWidth,s.FinWidth*.49f,p.y);
                    v.Add(new Vector3(Mathf.Lerp(back,front,p.x),Mathf.Lerp(s.FinBase,s.TailTop,p.y),side*(s.FinThickness*.5f+.006f+patch*.001f)));
                }
                for(int i=1;i<v.Count-1;i++)AircraftTriangle(v,t,0,i,i+1,Vector3.forward*side);
            });
        }
        static void BuildAirframeGear(Transform plane,AirframeSpec s,AirframePaint paint)
        {
            BuildAirframeBogie(plane,"Nose",s.NoseStart+.15f,0,1,s.Type==1?.19f:.15f,s.Type==0?.63f:.58f,paint.Trim);
            for(int side=-1;side<=1;side+=2)
            {
                if(s.Type==1)
                {
                    BuildAirframeBogie(plane,"Body main "+side,-1.10f,side*.61f,3,.21f,.84f,paint.Trim);
                    BuildAirframeBogie(plane,"Wing main "+side,-.55f,side*1.25f,2,.21f,s.WingY-.03f,paint.Trim);
                }
                else BuildAirframeBogie(plane,"Main "+side,-.66f,side*(s.Type==0?.74f:.66f),s.Type==2?2:1,.17f,s.WingY-.03f,paint.Trim);
            }
        }
        static void BuildAirframeBogie(Transform plane,string name,float x,float z,int axles,float radius,float strutTop,Color trim)
        {
            ModelBox(plane,name+" gear strut",new Vector3(x,(radius+strutTop)*.5f,z),new Vector3(.09f,strutTop-radius,.09f),trim,.027f,Quaternion.identity);
            if(axles>1)ModelBox(plane,name+" bogie beam",new Vector3(x,radius+.015f,z),new Vector3((axles-1)*radius*1.8f+.12f,.08f,.12f),trim,.025f,Quaternion.identity);
            for(int axle=0;axle<axles;axle++)for(int side=-1;side<=1;side+=2)
            {
                float wx=x+(axle-(axles-1)*.5f)*radius*1.8f,wz=z+side*.108f;
                string key="v2-tire-"+radius;
                var wheel=AircraftLathe(plane,name+" tire "+axle+" "+side,key,new[]{new Vector2(-.075f,0),new Vector2(-.073f,radius*.74f),new Vector2(-.045f,radius),new Vector2(.045f,radius),new Vector2(.073f,radius*.74f),new Vector2(.075f,0)},new Vector3(wx,radius,wz),Hex("263841"),AirportStyle.Finish.Matte);
                wheel.transform.localRotation=Quaternion.Euler(0,90,0);
                var hub=AircraftLathe(plane,name+" hub "+axle+" "+side,key+"hub",new[]{new Vector2(-.005f,0),new Vector2(-.005f,radius*.55f),new Vector2(.005f,radius*.55f),new Vector2(.005f,0)},new Vector3(wx,radius,wz+side*.077f),trim);
                hub.transform.localRotation=Quaternion.Euler(0,90,0);
            }
        }
    }
}
