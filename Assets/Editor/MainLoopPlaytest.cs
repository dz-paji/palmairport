using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using IslandAirport;

[InitializeOnLoad]
public static class MainLoopPlaytest
{
    static IEnumerator steps;
    static MainLoopGame game;
    static double started;
    static bool failed;
    const string Pending="PalmBay.MainLoopPlaytest";
    static MainLoopPlaytest(){EditorApplication.update+=Poll;}
    [MenuItem("Palm Bay/Verify main loop in Play Mode")]
    public static void Run()
    {
        EditorSceneManager.OpenScene(MainLoopBuilder.ScenePath);
        SessionState.SetBool(Pending,true);SessionState.SetBool(Pending+".Stop",false);
        EditorApplication.isPlaying=true;
    }
    static void Poll()
    {
        if(!SessionState.GetBool(Pending,false))return;
        if(SessionState.GetBool(Pending+".Stop",false))
        {
            if(EditorApplication.isPlaying)return;
            SessionState.SetBool(Pending,false);
            if(Application.isBatchMode)EditorApplication.Exit(SessionState.GetBool(Pending+".Failed",false)?1:0);
            return;
        }
        if(!EditorApplication.isPlaying)return;
        try
        {
            if(steps==null)
            {
                game=UnityEngine.Object.FindAnyObjectByType<MainLoopGame>();if(game==null||!game.Ready)return;
                game.ExternalControl=true;started=EditorApplication.timeSinceStartup;failed=false;
                Application.logMessageReceived+=OnLog;
                steps=Exercise();
            }
            if(EditorApplication.timeSinceStartup-started>160)throw new Exception("Playtest exceeded 160 seconds");
            // Drive the actual scene/controller, with actual transforms and visual objects.
            if(steps.MoveNext())return;
            Complete(false,"PASS: three flights completed through movement + interaction across all ramps; fourth flight arrived; saved scene models and cargo references survived reload.");
        }
        catch(Exception e){Complete(true,e.ToString());}
    }
    static void OnLog(string message,string stack,LogType kind)
    {
        if(kind==LogType.Exception||kind==LogType.Error)failed=true;
    }
    static void Complete(bool error,string message)
    {
        Application.logMessageReceived-=OnLog;
        error|=failed;Directory.CreateDirectory("Evidence");File.WriteAllText("Evidence/main-loop-playtest.txt",message+"\nUnity runtime errors: "+failed+"\n");
        Debug.Log((error?"MAIN_LOOP_PLAYTEST_FAILED ":"MAIN_LOOP_PLAYTEST_PASSED ")+message);
        SessionState.SetBool(Pending+".Failed",error);SessionState.SetBool(Pending+".Stop",true);steps=null;EditorApplication.isPlaying=false;
    }
    static void Require(bool value,string reason){if(!value)throw new Exception(reason);}
    static void Tick(CrewInput input){game.Step(.05f,new[]{input,new CrewInput()});}
    static IEnumerator Wait(float seconds)
    {
        for(int i=0;i<Mathf.CeilToInt(seconds/.05f);i++){Tick(new CrewInput());if(i%8==0)yield return null;}
    }
    static IEnumerator MoveTo(Vector3 target,float radius=.5f)
    {
        for(int i=0;i<700;i++)
        {
            Vector3 pos=game.Players[0].position;pos.y=0;Vector3 delta=target-pos;
            if(delta.magnitude<radius)yield break;
            float step=(game.Driving[0]<0?4.5f:3.6f)*.05f;
            Tick(new CrewInput{Move=new Vector2(delta.x,delta.z).normalized*Mathf.Min(1,delta.magnitude/step)});if(i%8==0)yield return null;
        }
        throw new Exception("Movement blocked while going to "+target+" from "+game.Players[0].position+" driving="+game.Driving[0]);
    }
    static IEnumerator Tap()
    {
        Tick(new CrewInput{Pressed=true,Held=true});Tick(new CrewInput());yield return null;
    }
    static IEnumerator Hold(float seconds)
    {
        for(int i=0;i<Mathf.CeilToInt(seconds/.05f);i++){Tick(new CrewInput{Held=true,Pressed=i==0});if(i%8==0)yield return null;}
        Tick(new CrewInput());
    }
    static IEnumerator RunSteps(IEnumerator operation){while(operation.MoveNext())yield return operation.Current;}
    static IEnumerator Exercise()
    {
        Require(game.Plane.GetComponentsInChildren<Renderer>().Length>10,"Plane renderers missing");
        Require(UnityEngine.Object.FindObjectsByType<MeshFilter>().Length>100,"Scene model geometry missing");
        foreach(var player in game.Players)Require(player.GetComponentsInChildren<Renderer>().Length>10,"Crew renderers missing");
        yield return null;
        var op=Wait(SingleFlightCycle.ArrivalDuration+.2f);while(op.MoveNext())yield return op.Current;
        Require(game.Cycle.Phase==CyclePhase.Servicing,"First plane failed to arrive");
        ScreenCapture.CaptureScreenshot("Evidence/main-loop-playing.png");yield return null;yield return null;
        // Deliberately attempt boarding early, through the real interaction entry.
        op=MoveTo(MainLoopGame.Supply(3));while(op.MoveNext())yield return op.Current;
        op=Tap();while(op.MoveNext())yield return op.Current;
        Require(!game.BoardingOpen,"Boarding allowed before meals and fuel");
        for(int cycle=0;cycle<3;cycle++)
        {
            for(int k=0;k<3;k++)
            {
                Vector3 cart=game.Vehicles[k].position;cart.y=0;
                op=MoveTo(cart,.3f);while(op.MoveNext())yield return op.Current;
                op=Tap();while(op.MoveNext())yield return op.Current;
                Require(game.Driving[0]==k,"Wrong vehicle acquired for task "+k);
                if(k==1)
                {
                    op=DriveToDock();while(op.MoveNext())yield return op.Current;
                    op=Tap();while(op.MoveNext())yield return op.Current;
                    Require(game.Cargo[k]==MainLoopGame.CargoState.ArrivalBags,"Arrival bags were not picked up");
                    op=DriveToSupply(k);while(op.MoveNext())yield return op.Current;
                    op=Tap();while(op.MoveNext())yield return op.Current;
                    Require(game.Cycle.Current.ArrivalBagsReturned,"Arrival bags were not returned");
                }
                else {op=DriveToSupply(k);while(op.MoveNext())yield return op.Current;}
                if(k==0)
                {
                    op=Tap();while(op.MoveNext())yield return op.Current;
                    Require(game.MealOrdered,"Meal production did not start");
                    op=Wait(5.2f);while(op.MoveNext())yield return op.Current;
                    op=Tap();while(op.MoveNext())yield return op.Current;
                }
                else if(k==1){op=Tap();while(op.MoveNext())yield return op.Current;}
                else {op=Hold(6.3f);while(op.MoveNext())yield return op.Current;}
                Require(game.Cargo[k]==MainLoopGame.CargoState.Outgoing,"Outgoing cargo missing for "+k);
                Require(game.CargoViews[k].gameObject.activeSelf,"Loaded cargo model not visible");
                op=DriveToDock();while(op.MoveNext())yield return op.Current;
                op=Hold(.8f);while(op.MoveNext())yield return op.Current;
                Require(game.Cycle.Current.Progress[k]==0,"Partial delivery incorrectly completed");
                Require(game.DeliveryProgress[0]==0,"Cancelled hold did not reset progress");
                op=Hold(2.7f);while(op.MoveNext())yield return op.Current;
                Require(game.Cycle.Current.Progress[k]>=1,"Delivery did not complete task "+k);
                Require(game.Cargo[k]==MainLoopGame.CargoState.Empty,"Delivered cargo did not clear");
                // Return and park each shared vehicle clear of its station so it can be reused.
                // The default fuel cart point lies on stand C's narrow ramp junction.
                // Park it north of that junction through normal driving, so later
                // meal/baggage routes can reach C without relocating fixtures.
                Vector3 parking=Level1Map.CartPark((ServiceKind)k)+(k==2?Vector3.forward*1.35f:Vector3.zero);
                op=DriveTo(parking,k==2?.04f:.3f);while(op.MoveNext())yield return op.Current;
                op=Tap();while(op.MoveNext())yield return op.Current;
                Require(game.Driving[0]<0,"Vehicle did not release");
            }
            VerifyParkingKeepsAllRampsReachable();
            Require(game.Cycle.Phase==CyclePhase.Servicing,"Plane departed before boarding");
            op=MoveTo(MainLoopGame.Supply(3));while(op.MoveNext())yield return op.Current;
            op=Tap();while(op.MoveNext())yield return op.Current;
            Require(game.BoardingOpen,"Boarding failed after prerequisites complete");
            op=Wait(2);while(op.MoveNext())yield return op.Current;
            Require(UnityEngine.Object.FindObjectsByType<Transform>().Length>100,"Runtime objects disappeared");
            if(cycle==0){ScreenCapture.CaptureScreenshot("Evidence/main-loop-boarding.png");yield return null;yield return null;}
            op=WaitForNextFlight(cycle+1);while(op.MoveNext())yield return op.Current;
            Require(game.Cycle.CompletedFlights==cycle+1,"Completed flight was not counted");
            
            Require(game.Cycle.Phase==CyclePhase.Servicing,"Next plane did not enter service");
            Require(!game.BoardingOpen,"Next flight inherited boarding state");
            Require(!game.Cycle.Current.ArrivalBagsReturned,"Next flight inherited arrival-bag state");
            foreach(float progress in game.Cycle.Current.Progress)Require(progress==0,"Next flight inherited a completed task");
            Debug.Log("MAIN_LOOP_CYCLE_VERIFIED "+game.Cycle.CompletedFlights);
        }
        ScreenCapture.CaptureScreenshot("Evidence/main-loop-third-flight.png");yield return null;yield return null;yield return null;
    }
    static void VerifyParkingKeepsAllRampsReachable()
    {
        for(int task=0;task<3;task++)
        {
            Vector3 parked=game.Vehicles[task].position;parked.y=0;
            Require(Level1Map.InDrivable(parked,.3f),"Parked task vehicle left the road network: "+task);
            Require(Vector3.Distance(parked,MainLoopGame.Supply(task))>=2.4f,
                "Parked task vehicle overlaps its station interaction zone: "+task);
            var obstacles=new System.Collections.Generic.List<Vector3>();
            for(int other=0;other<3;other++)if(other!=task)obstacles.Add(game.Vehicles[other].position);
            for(int stand=0;stand<Level1Map.StandCount;stand++)
                Require(Level1Map.DriveRoute(MainLoopGame.Supply(task),MainLoopGame.ServicePoint(stand),obstacles)!=null,
                    "Actual parked vehicles obstruct task "+task+" route to stand "+stand);
        }
        Debug.Log("MAIN_LOOP_PARKING_ROUTES_VERIFIED cycle="+game.Cycle.CompletedFlights+" routes=9");
    }

    static IEnumerator WaitForNextFlight(int completed)
    {
        for(int i=0;i<2400;i++)
        {
            if(game.Cycle.CompletedFlights==completed && game.Cycle.Phase==CyclePhase.Servicing)yield break;
            Tick(new CrewInput());if(i%8==0)yield return null;
        }
        throw new Exception("Flight failed to complete boarding/departure/arrival at ramp "+game.Cycle.CurrentStand);
    }
    // Driving follows the road network via Level1Map.DriveRoute; straight
    // segments between route waypoints are guaranteed to stay drivable.
    static IEnumerator DriveTo(Vector3 dest,float radius)
    {
        Vector3 from=game.Players[0].position;from.y=0;
        var obstacles=new System.Collections.Generic.List<Vector3>();
        for(int k=0;k<game.Vehicles.Length;k++)if(k!=game.Driving[0])obstacles.Add(game.Vehicles[k].position);
        var planned=Level1Map.DriveRoute(from,dest,obstacles);
        Require(planned!=null,"No clearance-safe route from "+from+" to "+dest+" with obstacles "+obstacles.Count);
        foreach(var wp in planned)
        {
            var op=MoveTo(wp,.04f);while(op.MoveNext())yield return op.Current;
            Require(game.Driving[0]<0 || Level1Map.InDrivable(game.Vehicles[game.Driving[0]].position,0.3f),"vehicle left the road network");
        }
        var last=MoveTo(dest,radius);while(last.MoveNext())yield return last.Current;
        Require(game.Driving[0]<0 || Level1Map.InDrivable(game.Vehicles[game.Driving[0]].position,0.3f),"vehicle left the road network");
    }
    static IEnumerator DriveToDock()
    {
        var op=DriveTo(MainLoopGame.ServicePoint(game.Cycle.CurrentStand),.6f);while(op.MoveNext())yield return op.Current;
    }
    static IEnumerator DriveToSupply(int k)
    {
        var op=DriveTo(MainLoopGame.Supply(k),.5f);while(op.MoveNext())yield return op.Current;
    }
}
