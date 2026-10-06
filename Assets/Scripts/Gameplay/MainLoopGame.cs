using System;
using System.Collections.Generic;
using UnityEngine;

namespace IslandAirport
{
    /// <summary>The first playable slice: one aircraft, four physical jobs, repeat.</summary>
    public sealed class MainLoopGame : MonoBehaviour
    {
        public Transform Plane;
        public Transform[] Players;
        public Transform[] Vehicles;
        public Transform[] CargoViews;
        public Camera View;
        public bool ExternalControl;
        public SingleFlightCycle Cycle { get; private set; }
        public bool Ready { get; private set; }
        public bool Paused { get; private set; }
        public bool MealOrdered { get; private set; }
        public float MealProgress { get; private set; }
        public float FuelReserve { get; private set; }
        public bool BoardingOpen { get; private set; }
        public string Message { get; private set; }
        public readonly int[] Driving = {-1,-1};
        public readonly float[] DeliveryProgress = {0,0};
        public readonly CargoState[] Cargo = new CargoState[3];
        public enum CargoState {Empty, ArrivalBags, Outgoing}
        public static readonly string[] TaskNames={"餐食","行李","燃油","登机"};
        // All level geometry comes from the shared level-1 map; the current
        // flight's ramp rotates through the three stands.
        public int Stand { get { return Cycle.CurrentStand; } }
        public static Vector3 ServicePoint(int stand) { return Level1Map.Dock(stand); }
        public static Vector3 Supply(int k) { return Level1Map.Station((ServiceKind)k); }
        readonly int[] owners={-1,-1,-1};
        int pumpOwner=-1, sequence;
        readonly bool[] traffic={false,false};
        readonly List<Traveler> travelers=new List<Traveler>();
        sealed class Traveler {public Transform View; public float Delay;public int Waypoint;}
        Font font; GUIStyle textStyle,buttonStyle;

        void Start()
        {
            SceneLayoutSync.Ensure(this);
            if(!Plane || !View || Players==null || Players.Length!=2 || Vehicles==null || Vehicles.Length!=3 || CargoViews==null || CargoViews.Length!=3)
                throw new InvalidOperationException("MainLoop scene references are incomplete. Use Palm Bay > Rebuild MainLoop scene (debug).");
            Cycle=new SingleFlightCycle();sequence=Cycle.Sequence;
            font=Font.CreateDynamicFontFromOSFont(new[]{"PingFang SC","Microsoft YaHei","Noto Sans CJK SC","Arial Unicode MS","Arial"},20);
            Message="飞机正在滑入机位；你可以先去餐食站下单。";
            foreach(var cargo in CargoViews)cargo.gameObject.SetActive(false);
            Application.targetFrameRate=60;
            Ready=true;UpdatePlane();
        }
        void Update()
        {
            if(!Ready || ExternalControl)return;
            if(Input.GetKeyDown(KeyCode.Escape))Paused=!Paused;
            if(Paused)return;
            var input=new CrewInput[2];
            input[0]=new CrewInput {Move=new Vector2((Input.GetKey(KeyCode.D)?1:0)-(Input.GetKey(KeyCode.A)?1:0),(Input.GetKey(KeyCode.W)?1:0)-(Input.GetKey(KeyCode.S)?1:0)),Pressed=Input.GetKeyDown(KeyCode.E),Held=Input.GetKey(KeyCode.E)};
            input[1]=new CrewInput {Move=new Vector2((Input.GetKey(KeyCode.RightArrow)?1:0)-(Input.GetKey(KeyCode.LeftArrow)?1:0),(Input.GetKey(KeyCode.UpArrow)?1:0)-(Input.GetKey(KeyCode.DownArrow)?1:0)),Pressed=Input.GetKeyDown(KeyCode.RightShift),Held=Input.GetKey(KeyCode.RightShift)};
            Step(Time.deltaTime,input);
        }
        public void Step(float dt,CrewInput[] input)
        {
            if(!Ready || Paused || input==null || input.Length!=2 || dt<=0 || float.IsNaN(dt) || float.IsInfinity(dt))return;
            Cycle.Tick(dt);
            if(sequence!=Cycle.Sequence)
            {
                sequence=Cycle.Sequence;BoardingOpen=false;
                foreach(var person in travelers)Destroy(person.View.gameObject);travelers.Clear();
                for(int k=0;k<3;k++)SetCargo(k,CargoState.Empty);
                DeliveryProgress[0]=DeliveryProgress[1]=0;
                Message="下一架飞机进场，滑向机位 "+"ABC"[Cycle.CurrentStand]+"；空车、站点和队友可以继续工作。";
            }
            UpdatePlane();
            if(MealOrdered)MealProgress=Mathf.Min(1,MealProgress+dt/5);
            if(pumpOwner>=0 && (!input[pumpOwner].Held || !Near(Players[pumpOwner].position,Supply(2)) || (Driving[pumpOwner]>=0 && Cargo[Driving[pumpOwner]]!=CargoState.Empty)))pumpOwner=-1;
            for(int p=0;p<2;p++){Move(p,input[p].Move,Mathf.Min(dt,.05f));Act(p,input[p],dt);}
            UpdateTravelers(dt);
        }
        void UpdatePlane()
        {
            int stand=Cycle.CurrentStand;
            Vector3[] arrivalPath=Level1Map.TaxiInPath(stand);
            if(Cycle.Phase==CyclePhase.Arriving)
            {
                // Land on the runway, roll to the stand's exit, taxi west
                // along the taxiway stub into the ramp.
                float t=Mathf.SmoothStep(0,1,Mathf.Clamp01(Cycle.PhaseProgress/SingleFlightCycle.ArrivalDuration));
                Vector3 dir;Plane.position=PointAlong(arrivalPath,t,out dir);
                if(dir.sqrMagnitude>.001f)Plane.rotation=AircraftMotion.NoseRotation(dir);
            }
            else if(Cycle.Phase==CyclePhase.Departing)
            {
                // A departure is deliberately staged so the aircraft first
                // backs out of the ramp, turns at the taxiway entry, then
                // follows the authored taxiway tangents before lifting off.
                var path=Level1Map.TaxiOutPath(stand);
                var pushback=AircraftMotion.BuildPushbackPath(arrivalPath,path);
                int forwardStart=Mathf.Clamp(AircraftMotion.PushbackEndIndex(path),0,path.Length-1);
                var forwardPath=AircraftMotion.BuildForwardTaxiPath(path,forwardStart,pushback);
                float t=Mathf.Clamp01(Cycle.PhaseProgress/SingleFlightCycle.DepartureDuration);
                const float pushbackEnd=.58f;
                const float turnEnd=.65f;
                const float taxiEnd=.86f;
                Vector3 forward=AircraftMotion.FirstDirection(forwardPath,0,
                    AircraftMotion.FinalDirection(arrivalPath));
                Vector3 reverseEnd=AircraftMotion.FinalDirection(pushback,-forward);
                if(t<pushbackEnd)
                {
                    Vector3 dir;Plane.position=PointAlong(pushback,t/pushbackEnd,out dir);
                    if(dir.sqrMagnitude>.001f)Plane.rotation=AircraftMotion.NoseRotation(-dir);
                }
                else if(t<turnEnd)
                {
                    // The final reverse curve already points the nose along
                    // the next forward tangent. Hold there briefly so the
                    // pushback, alignment, and forward taxi read as three
                    // distinct actions without an in-place rotation.
                    Plane.position=pushback[pushback.Length-1];
                    Plane.rotation=AircraftMotion.NoseRotation(-reverseEnd);
                }
                else if(t<taxiEnd)
                {
                    Vector3 dir;
                    Plane.position=AircraftMotion.PointAlong(forwardPath,
                        Mathf.InverseLerp(turnEnd,taxiEnd,t),out dir,forward);
                    if(dir.sqrMagnitude>.001f)Plane.rotation=AircraftMotion.NoseRotation(dir);
                }
                else
                {
                    float e=Mathf.InverseLerp(taxiEnd,1,t)*2.2f;
                    Vector3 liftDirection=AircraftMotion.FinalDirection(forwardPath,forward);
                    Plane.rotation=AircraftMotion.NoseRotation(liftDirection);
                    Vector3 runwayStart=forwardPath[forwardPath.Length-1];
                    Plane.position=runwayStart+liftDirection*(e*e*6f)+Vector3.up*(e*e*2.5f);
                }
            }
            else
            {
                // Keep the parked aircraft facing the direction it used to
                // enter the ramp. The next departure then has a real reverse
                // pushback instead of snapping to an arbitrary world heading.
                Plane.position=Level1Map.PlanePark(stand);
                Plane.rotation=AircraftMotion.NoseRotation(AircraftMotion.FinalDirection(arrivalPath));
            }
        }
        // Walk a polyline by normalized distance, returning the segment direction.
        static Vector3 PointAlong(Vector3[] path,float t,out Vector3 dir)
        {
            return AircraftMotion.PointAlong(path,t,out dir);
        }
        static Vector3 Ground(Vector3 p){p.y=0;return p;}
        static bool Near(Vector3 a,Vector3 b,float radius=1.5f){return Vector3.Distance(Ground(a),Ground(b))<radius;}
        void Move(int p,Vector2 movement,float dt)
        {
            Vector3 direction=new Vector3(movement.x,0,movement.y);if(direction.sqrMagnitude>1)direction.Normalize();
            Vector3 current=Ground(Players[p].position),next=current+direction*(Driving[p]<0?4.5f:3.6f)*dt;
            next.x=Mathf.Clamp(next.x,Level1Map.WalkMin.x,Level1Map.WalkMax.x);next.z=Mathf.Clamp(next.z,Level1Map.WalkMin.y,Level1Map.WalkMax.y);
            // Keep clear of the active ramp and of the aircraft itself while
            // it is moving (landing roll, taxi, departure).
            if(Cycle.Phase!=CyclePhase.Departing)
            {
                if(Level1Map.InsideParkedAircraftArea(next,Cycle.CurrentStand))next=current;
            }
            if(Near(next,Plane.position,1.9f))next=current;
            traffic[p]=false;
            if(Driving[p]>=0)
            {
                // Vehicles must stay on the road network.
                next=Level1Map.ClampToDrivable(current,next);
                foreach(var t in travelers)if(t.Delay<=0 && Near(next,t.View.position,.78f)){next=current;traffic[p]=true;break;}
                for(int k=0;k<3;k++)if(k!=Driving[p] && Near(next,Vehicles[k].position,.92f)){next=current;break;}
            }
            if(direction.sqrMagnitude>.01f)Players[p].rotation=Quaternion.Slerp(Players[p].rotation,Quaternion.LookRotation(direction),dt*12);
            Players[p].position=next+Vector3.up*(Driving[p]>=0?.45f:0);
            if(Driving[p]>=0){Vehicles[Driving[p]].position=next;Vehicles[Driving[p]].rotation=Players[p].rotation;}
        }
        public string Context(int p)
        {
            if(traffic[p])return "旅客正在过路，请让行";
            int k=Driving[p];
            if(k>=0)
            {
                if(Near(Players[p].position,Supply(k)))
                {
                    if(Cargo[k]==CargoState.ArrivalBags)return "点按：交还到达行李";
                    if(Cargo[k]!=CargoState.Empty)return "物资已装车，送往飞机左侧接驳点";
                    if(k==0)return !MealOrdered?"点按：下单备餐":MealProgress<1?"备餐中，可以先去做别的事":"点按：把餐食装车";
                    if(k==1)return !Cycle.Current.ArrivalBagsReturned?"先开空车去飞机卸下到达行李":"点按：装载出发行李";
                    return FuelReserve<1?"按住：补充燃油":"按住：把燃油装车";
                }
                if(Near(Players[p].position,ServicePoint(Stand)))
                {
                    if(Cycle.Phase!=CyclePhase.Servicing)return "飞机正在滑入机位 · 停稳后再作业";
                    if(Cargo[k]==CargoState.ArrivalBags)return "到达行李需要送回左侧行李站";
                    if(Cargo[k]==CargoState.Outgoing)return "按住：交付"+TaskNames[k];
                    if(k==1&&!Cycle.Current.ArrivalBagsReturned)return "点按：卸下到达行李";
                    return "空车，点按放开或开回补给站";
                }
                return "点按：放开车辆，交给搭档";
            }
            if(Near(Players[p].position,Supply(0))&&!MealOrdered)return "点按：下单备餐";
            for(int i=0;i<3;i++)if(owners[i]<0 && Near(Players[p].position,Vehicles[i].position,1.65f))return "点按：驾驶"+TaskNames[i]+"车";
            if(Near(Players[p].position,Supply(2)))return "按住：提前补油，让搭档来接车";
            if(Near(Players[p].position,Supply(3)))return BoardingOpen?"旅客正在登机":"点按：放行旅客（需完成餐食和燃油）";
            if(Near(Players[p].position,Supply(0)))return MealProgress>=1?"餐食已备好，开餐车来取":"备餐中，可以先去做别的事";
            return "靠近站点或车辆，查看交互提示";
        }
        void Act(int p,CrewInput input,float dt)
        {
            if(!input.Held&&!input.Pressed){DeliveryProgress[p]=0;return;}
            int k=Driving[p];Vector3 position=Players[p].position;
            if(k<0)
            {
                if(input.Pressed)
                {
                    if(Near(position,Supply(0))&&!MealOrdered){OrderMeal();return;}
                    for(int i=0;i<3;i++)if(owners[i]<0&&Near(position,Vehicles[i].position,1.65f)){Driving[p]=i;owners[i]=p;Players[p].position=Vehicles[i].position+Vector3.up*.45f;return;}
                    if(Near(position,Supply(3))){OpenBoarding();return;}
                    if(Near(position,Supply(0))){OrderMeal();return;}
                }
                if(input.Held&&Near(position,Supply(2)))Pump(p,dt);
                return;
            }
            if(Near(position,Supply(k)))
            {
                DeliveryProgress[p]=0;
                if(Cargo[k]==CargoState.ArrivalBags && input.Pressed){if(Cycle.Simulation.ReturnArrivalBags(Cycle.Current)){SetCargo(k,CargoState.Empty);Message="到达行李已交还，再点按装载出发行李。";}return;}
                if(Cargo[k]!=CargoState.Empty){if(input.Pressed)Release(p);return;}
                if(k==0 && !MealOrdered && input.Pressed){OrderMeal();return;}
                if(k==2 && input.Held && FuelReserve<1){Pump(p,dt);return;}
                if(Cycle.Phase!=CyclePhase.Servicing){if(input.Pressed)Message="可以提前备餐、补油；飞机停稳后再装载。";return;}
                if(Cycle.Current.Progress[k]>=1){if(input.Pressed)Message=TaskNames[k]+"已完成，可以换一项任务。";return;}
                if(k==0 && input.Pressed && MealProgress>=1){SetCargo(k,CargoState.Outgoing);MealOrdered=false;MealProgress=0;Message="餐食已装车，送去飞机接驳点。";}
                if(k==1 && input.Pressed){if(Cycle.Current.ArrivalBagsReturned){SetCargo(k,CargoState.Outgoing);Message="出发行李已装车。";}else Message="先去飞机取到达行李，送回行李站。";}
                if(k==2 && input.Held && FuelReserve>=1){SetCargo(k,CargoState.Outgoing);FuelReserve=0;pumpOwner=-1;Message="油车已装满，送去飞机接驳点。";}
                return;
            }
            if(Near(position,ServicePoint(Stand)))
            {
                if(Cycle.Phase!=CyclePhase.Servicing){DeliveryProgress[p]=0;return;}
                if(Cargo[k]==CargoState.Empty)
                {
                    if(k==1&&!Cycle.Current.ArrivalBagsReturned && input.Pressed){SetCargo(k,CargoState.ArrivalBags);Message="到达行李在车上了，送回行李站。";}
                    else if(input.Pressed)Release(p);
                    return;
                }
                if(Cargo[k]==CargoState.ArrivalBags){if(input.Pressed)Message="这是到达行李，请送回左侧行李站。";return;}
                if(input.Held)DeliveryProgress[p]+=dt/2.5f;
                if(DeliveryProgress[p]>=1 && Cycle.Simulation.TryAdvance(Cycle.Current,(ServiceKind)k,1)){SetCargo(k,CargoState.Empty);DeliveryProgress[p]=0;Message=TaskNames[k]+"完成！";}
                return;
            }
            DeliveryProgress[p]=0;if(input.Pressed)Release(p);
        }
        void Release(int p){owners[Driving[p]]=-1;Driving[p]=-1;Players[p].position=Ground(Players[p].position);DeliveryProgress[p]=0;}
        void SetCargo(int k,CargoState state){Cargo[k]=state;CargoViews[k].gameObject.SetActive(state!=CargoState.Empty);}
        void OrderMeal(){if(!MealOrdered){MealOrdered=true;MealProgress=0;Message="开始备餐，5 秒后取货；现在可以移动做别的事。";}else Message=MealProgress>=1?"出货口有餐食，请开餐车来取。":"正在备餐。";}
        void Pump(int p,float dt){if(pumpOwner<0||pumpOwner==p){pumpOwner=p;FuelReserve=Mathf.Min(1,FuelReserve+dt/6);}else Message="搭档正在操作油泵。";}
        void OpenBoarding()
        {
            if(BoardingOpen)return;
            if(Cycle.Phase!=CyclePhase.Servicing || Cycle.Current.Progress[0]<1 || Cycle.Current.Progress[2]<1){Message="餐食和燃油完成后才能放行旅客。";return;}
            BoardingOpen=true;Message="旅客沿人行道前往登机点，过斑马线时记得让行。";
            for(int i=0;i<8;i++){var t=new Traveler {View=AirportWorld.CreatePassenger("Traveler "+(i+1),AirportWorld.Hex(i%2==0?"E4A1A6":"99B5D8"),Supply(3),i),Delay=i*.65f,Waypoint=1};t.View.localScale=Vector3.one*.65f;t.View.gameObject.SetActive(false);travelers.Add(t);}
        }
        void UpdateTravelers(float dt)
        {
            if(Cycle.Phase!=CyclePhase.Servicing)return;
            Vector3[] path=Level1Map.PavementPath(Cycle.CurrentStand);
            for(int i=travelers.Count-1;i>=0;i--)
            {
                Traveler t=travelers[i];t.Delay-=dt;if(t.Delay>0)continue;t.View.gameObject.SetActive(true);
                // Passengers follow the pavement and only board at the
                // stand's boarding point (the path's last waypoint).
                Vector3 target=path[Mathf.Min(t.Waypoint,path.Length-1)];
                Vector3 direction=target-t.View.position;if(direction.sqrMagnitude>.001f)t.View.rotation=Quaternion.LookRotation(direction);
                t.View.position=Vector3.MoveTowards(t.View.position,target,dt*1.7f);
                if(Vector3.Distance(t.View.position,target)<.08f)
                {
                    if(t.Waypoint<path.Length-1)t.Waypoint++;
                    else {Cycle.Simulation.TryAdvance(Cycle.Current,ServiceKind.Boarding,.125f);Destroy(t.View.gameObject);travelers.RemoveAt(i);}
                }
            }
        }
        void Panel(Rect r,Color c){GUI.color=c;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=Color.white;}
        void Label(Rect r,string text,int size=20){textStyle.fontSize=size;GUI.Label(r,text,textStyle);}
        void OnGUI()
        {
            if(!Ready)return;
            if(textStyle==null){textStyle=new GUIStyle(GUI.skin.label){font=font,wordWrap=true,normal={textColor=Color.white}};buttonStyle=new GUIStyle(GUI.skin.button){font=font,fontSize=18};}
            float scale=Mathf.Min(Screen.width/1600f,Screen.height/1000f);GUI.matrix=Matrix4x4.TRS(new Vector3((Screen.width-1600*scale)/2,(Screen.height-1000*scale)/2),Quaternion.identity,Vector3.one*scale);
            Color dark=new Color(.07f,.16f,.2f,.94f);Panel(new Rect(28,24,1544,136),dark);
            string phase=Cycle.Phase==CyclePhase.Arriving?"正在到达":Cycle.Phase==CyclePhase.Departing?"正在起飞":"地勤作业";
            Label(new Rect(48,35,1500,36),"主循环 01    "+Cycle.Current.Id+" · "+phase+"       已送走 "+Cycle.CompletedFlights+" 架",24);
            for(int k=0;k<4;k++){float progress=Cycle.Current.Progress[k];Panel(new Rect(48+k*379,89,359,47),progress>=1?new Color(.13f,.5f,.39f):new Color(.2f,.3f,.35f));Label(new Rect(60+k*379,96,337,35),TaskNames[k]+"   "+(progress>=1?"完成":progress>0?Mathf.RoundToInt(progress*100)+"%":"待完成"),21);}
            Panel(new Rect(28,839,1544,136),dark);Label(new Rect(48,847,1480,30),Message,20);
            for(int p=0;p<2;p++)
            {
                Label(new Rect(48+p*772,887,738,75),(p==0?"P1  WASD 移动 · E 交互":"P2  方向键移动 · 右 Shift 交互")+"\n"+Context(p),18);
                if(DeliveryProgress[p]>0)Panel(new Rect(48+p*772,962,720*DeliveryProgress[p],5),new Color(.3f,.9f,.64f));
            }
            Label(new Rect(35,177,720,30),"到达 → 四类地勤任务 → 起飞 → 下一架    /    Esc 暂停",18);
            foreach(int k in new[]{0,1,2,3})
            {
                Vector3 screen=View.WorldToScreenPoint(Supply(k)+Vector3.up*1.8f);float x=(screen.x-(Screen.width-1600*scale)/2)/scale,y=(Screen.height-screen.y-(Screen.height-1000*scale)/2)/scale;
                Panel(new Rect(x-76,y-12,152,35),dark);Label(new Rect(x-70,y-8,146,30),TaskNames[k]+(k==0&&MealOrdered?" "+Mathf.RoundToInt(MealProgress*100)+"%":k==2?" "+Mathf.RoundToInt(FuelReserve*100)+"%":"站"),17);
            }
            if(Paused){Panel(new Rect(540,378,520,150),dark);Label(new Rect(575,401,450,40),"已暂停 · 按 Esc 继续",28);if(GUI.Button(new Rect(575,459,450,44),"继续作业",buttonStyle))Paused=false;}
        }
    }
}
