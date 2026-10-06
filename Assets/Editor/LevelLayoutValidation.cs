using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using IslandAirport;

/// <summary>Checks the geometry actually consumed by both scenes, not a duplicate test map.</summary>
public static class LevelLayoutValidation
{
    static int assertions;
    static void Require(bool condition,string reason)
    {
        assertions++;if(!condition)throw new Exception(reason);
    }
    static bool InRect(Vector3 p,Rect r,float tolerance=.015f)
    {
        return p.x>=r.xMin-tolerance&&p.x<=r.xMax+tolerance&&p.z>=r.yMin-tolerance&&p.z<=r.yMax+tolerance;
    }
    static bool InPolygon(Vector3 p,Vector3[] polygon)
    {
        float low=0,high=0;
        for(int i=0;i<polygon.Length;i++)
        {
            Vector3 a=polygon[i],b=polygon[(i+1)%polygon.Length];
            float cross=(b.x-a.x)*(p.z-a.z)-(b.z-a.z)*(p.x-a.x);
            low=Mathf.Min(low,cross);high=Mathf.Max(high,cross);
        }
        return low>=-.025f||high<=.025f;
    }
    static bool AircraftSurface(Vector3 p)
    {
        if(InRect(p,Level1Map.Runway))return true;
        foreach(var r in Level1Map.Ramps)if(InRect(p,r))return true;
        foreach(var poly in Level1Map.TaxiwaySurfaces)if(InPolygon(p,poly))return true;
        return false;
    }
    static bool PavementSurface(Vector3 p)
    {
        foreach(var r in Level1Map.PavementSurfaces)if(InRect(p,r,.055f))return true;
        for(int s=0;s<3;s++)if(Vector3.Distance(p,Level1Map.BoardingPoint(s))<.3f)return true;
        return false;
    }
    public static void Validate()
    {
        assertions=0;
        Require(Level1Map.RoadSurfaces.Length==6,"Figma road group must retain its six members");
        Require(Level1Map.PavementSurfaces.Length==5,"Figma pavement group must retain its five members");
        Require(Level1Map.TaxiwaySurfaces.Length==9,"Figma taxiway group must retain its nine members");
        Require(Level1Map.Ramps[1].xMin<Level1Map.Ramps[0].xMin,"Middle ramp must extend west");
        Require(Level1Map.InDrivable(Level1Map.FromDesign(171,72.5f),.35f),"Facility/spine seam must accept full vehicle footprint");
        Require(Level1Map.InDrivable(Level1Map.FromDesign(235,76.5f),.35f),"Spine/ramp branch seam must accept full vehicle footprint");
        Require(!Level1Map.InDrivable(Level1Map.FromDesign(235,90),.35f),"Concave outside corner must reject overlapping footprint");
        Require(!Level1Map.InDrivable(Level1Map.FromDesign(550,42.5f),.35f),"Taxiway must not become drivable");
        Require(!Level1Map.InDrivable(Level1Map.FromDesign(400,157.5f),.35f),"Pavement-only area must not become drivable");
        var jumpFrom=Level1Map.FromDesign(203,100);var jumpTo=Level1Map.PlanePark(2);
        var jump=Level1Map.ClampToDrivable(jumpFrom,jumpTo);
        Require(Vector3.Distance(jump,jumpTo)>.1f,"Large movement must not jump over disconnected road edges");
        for(int i=0;i<=100;i++)Require(Level1Map.InDrivable(Vector3.Lerp(jumpFrom,jump,i/100f),.35f),"Clamped movement must stay inside the road along its whole segment");
        Vector3 shared=Level1Map.FromDesign(198,245);
        Require(PavementSurface(shared)&&Level1Map.InDrivable(shared,0),"Ramp 3 pavement must share the road strip in the design");
        for(int stand=0;stand<3;stand++)
        {
            foreach(var path in new[]{Level1Map.TaxiInPath(stand),Level1Map.TaxiOutPath(stand)})
                for(int seg=1;seg<path.Length;seg++)
                {
                    // Approach is airborne before touchdown; check every ground segment.
                    if(path[seg-1].y>.01f)continue;
                    for(int i=0;i<=100;i++)Require(AircraftSurface(Vector3.Lerp(path[seg-1],path[seg],i/100f)),"Aircraft route leaves taxi surfaces at stand "+stand+" segment "+seg);
                }
            var arrival=Level1Map.TaxiInPath(stand);var departure=Level1Map.TaxiOutPath(stand);
            var reverse=AircraftMotion.BuildPushbackPath(arrival,departure);
            var forward=AircraftMotion.BuildForwardTaxiPath(departure,AircraftMotion.PushbackEndIndex(departure),reverse);
            foreach(var taxi in new[]{reverse,forward})for(int seg=1;seg<taxi.Length;seg++)for(int i=0;i<=40;i++)
                Require(AircraftSurface(Vector3.Lerp(taxi[seg-1],taxi[seg],i/40f)),"Pushback/forward curve leaves taxiway at stand "+stand+" segment "+seg);
            Require(Vector3.Dot(-AircraftMotion.FinalDirection(reverse),AircraftMotion.FirstDirection(forward))>.95f,"Pushback must finish with nose aligned for forward taxi at stand "+stand);
            var walk=Level1Map.PavementPath(stand);
            for(int seg=1;seg<walk.Length;seg++)for(int i=0;i<=100;i++)
                Require(PavementSurface(Vector3.Lerp(walk[seg-1],walk[seg],i/100f)),"Passenger leaves pavement at stand "+stand+" segment "+seg);
            foreach(ServiceKind kind in new[]{ServiceKind.Meals,ServiceKind.Baggage,ServiceKind.Fuel})
            {
                Require(Level1Map.InDrivable(Level1Map.CartPark(kind),.35f),"Cart park outside road");
                ValidateDrive(Level1Map.CartPark(kind),Level1Map.Station(kind));
                ValidateDrive(Level1Map.Station(kind),Level1Map.Dock(stand));
                ValidateDrive(Level1Map.Dock(stand),Level1Map.CartPark(kind));
            }
        }
        Directory.CreateDirectory("Evidence");
        File.WriteAllText("Evidence/level-layout-validation.txt","PASS: "+assertions+" assertions; grouped road joins, all station/dock routes, all passenger routes, taxiway ground routes and shared ramp-3 pavement.\n");
        Debug.Log("LEVEL_LAYOUT_VALIDATED assertions="+assertions);
    }
    public static void ValidateRoads()
    {
        assertions=0;
        for(int stand=0;stand<3;stand++)foreach(ServiceKind kind in new[]{ServiceKind.Meals,ServiceKind.Baggage,ServiceKind.Fuel})
        {
            ValidateDrive(Level1Map.CartPark(kind),Level1Map.Station(kind));
            ValidateDrive(Level1Map.Station(kind),Level1Map.Dock(stand));
            ValidateDrive(Level1Map.Dock(stand),Level1Map.CartPark(kind));
        }
        Debug.Log("LEVEL_ROADS_VALIDATED assertions="+assertions);
    }
    static void ValidateDrive(Vector3 start,Vector3 end)
    {
        Vector3 position=start;
        var planned=Level1Map.DriveRoute(start,end);
        Require(planned!=null,"No drivable route from "+start+" to "+end);
        var route=new System.Collections.Generic.List<Vector3>(planned);route.Add(end);
        foreach(var waypoint in route)
        {
            int steps=0;
            while(Vector3.Distance(position,waypoint)>.001f&&steps++<2000)
            {
                var next=Vector3.MoveTowards(position,waypoint,.08f);
                var clamped=Level1Map.ClampToDrivable(position,next);
                Require(Vector3.Distance(clamped,next)<.001f,"Road route blocked from "+position+" toward "+waypoint);
                Require(Level1Map.InDrivable(next,.35f),"Vehicle footprint leaves connected road");position=next;
            }
            Require(steps<2000,"Drive route did not finish");
        }
    }
    public static void CaptureTopDown()
    {
        EditorSceneManager.OpenScene(MainLoopBuilder.ScenePath);
        var game=UnityEngine.Object.FindObjectOfType<MainLoopGame>();
        Level1Map.ConfigureCamera(game.View,true);
        Capture(game.View,"Evidence/level-layout-top-down.png");
    }
    public static void Capture(Camera camera,string path)
    {
        var rt=new RenderTexture(1748,804,24);var oldTarget=camera.targetTexture;var oldActive=RenderTexture.active;
        camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
        var image=new Texture2D(1748,804,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,1748,804),0,0);image.Apply();
        Directory.CreateDirectory("Evidence");File.WriteAllBytes(path,image.EncodeToPNG());
        camera.targetTexture=oldTarget;RenderTexture.active=oldActive;UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(rt);
    }
}
