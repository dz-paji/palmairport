using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IslandAirport
{
    // Input is deliberately isolated so a mobile joystick can feed the same commands.
    public struct CrewInput { public Vector2 Move; public bool Pressed, Held, Cycle; }
    public sealed class Crew
    {
        public int Index, Selected; public Transform Visual; public Cart Cart; public string Hint = "", Name = "";
        public float NextActionAt; public Vector3 Position; public Color Color; public bool Bot, BlockedByTraffic;
        internal Vector3[] BotRoute; internal Vector3 BotRouteTarget, BotLastPosition;
        internal int BotWaypoint; internal Cart BotRouteCart; internal float BotLastMovedAt;
    }
    /// <summary>共享车辆的表现层壳：只留位置/Visual/Owner（任务域状态在 ShiftSim.Carts）。</summary>
    public sealed class Cart
    {
        public ServiceKind Kind; public Transform Visual, Cargo; public Crew Owner; public Vector3 Position; public bool Slipping;
    }
    public sealed partial class AirportGame : MonoBehaviour
    {
        /// <summary>任务领域状态（本地/host 为权威实例；Remote 为只读镜像）。永不为 null。</summary>
        public ShiftSim Shift { get { return shift; } }
        /// <summary>航班/进度/计分（组合在 ShiftSim 内；Remote 为只读镜像；有限班岗含权威计时与结算）。</summary>
        public AirportSimulation Sim { get { return shift == null ? null : shift.Sim; } }
        ShiftSim shift;
        public readonly List<Crew> Crew = new List<Crew>();
        public readonly List<Cart> Carts = new List<Cart>();
        public bool Started, Sandbox, Remote, Paused, Help, Solo = true;
        public bool NetworkShift { get; private set; }
        public int LocalSeat { get { var room=AppState.Ensure().Room; return NetworkShift && room!=null ? Math.Max(0,room.LocalSeat) : Remote ? RemoteSeat : 0; } }
        public int RoundId { get { var room=AppState.Ensure().Room; return NetworkShift && room!=null ? room.RoundId : 0; } }
        public bool MatchFinished { get { return Sim!=null && Sim.Finished; } }
        public float DisplayElapsed { get { return Sim==null?0:Remote&&!NetworkShift?Shift.MirrorElapsed:Sim.Elapsed; } }
        public float DisplayRemaining { get { return Sandbox || Remote&&!NetworkShift ? float.PositiveInfinity : Sim==null?300:Sim.Remaining; } }
        public int DisplayScore { get { return Sim==null?0:Remote&&!NetworkShift?Shift.MirrorScore:Sim.Score; } }
        public int DisplayStars { get { return Sim==null?0:Sim.Stars; } }
        public int DisplayDeparted { get { return Sim==null?0:Sim.DepartedCount; } }
        public bool CanRetry { get { var room=AppState.Ensure().Room; return MatchFinished && (!NetworkShift || room!=null&&room.IsHost); } }
        public string MigrationStatus { get; private set; } = string.Empty;
        public bool ReplayAvailable { get { return MatchFinished && replay.Count>0 && (!NetworkShift || networkReplayReady); } }
        public int ReplayFrameCount { get { return replay.Count; } }
        public string ReplayStatus { get; private set; } = string.Empty;
        public bool ReplayReady { get { return networkReplayReady; } }
        bool networkReplayReady, restoringPlanes;
        string localMatchId = string.Empty;
        public bool HumanSeat(int seat) { return seat>=0&&seat<Crew.Count&&!Crew[seat].Bot; }
        public float ReplayTime { get { return replayTime; } }
        public string ReplaySampleHash(int index) { return netSession==null?string.Empty:netSession.ReplayHash(index); }
        public void SetReplayStatus(string status) { ReplayStatus=status; }
        public void SetNetworkReplayClock(float time, bool ready)
        {
            if(ready && time<replayTime) { replayIndex=0;replayLastEventSeq=0;ClearReplayNotice(); }
            networkReplayReady=ready;replayTime=Mathf.Max(0,time);
        }
        public int RemoteSeat = -1;
        public string[] RemoteLabels = new string[2];
        public SnapshotBuffer Snaps { get; private set; }
        NetSession netSession;
        int remoteToastSeq = int.MinValue;
        public string Toast = "欢迎来到 PALM BAY，今天也一起把飞机送走。";
        public float ToastTime = 6;
        public string ReplayNotice { get; private set; } = string.Empty;
        public float ReplayNoticeRemaining { get; private set; }
        public int ReplayNoticeCount { get; private set; }
        public int Deliveries, TrafficStops;
        public string PlayerName = "地勤 01";
        public string LaunchRoomName = string.Empty;
        public Camera View;

        Transform[] planes = new Transform[Level1Map.StandCount];
        Flight[] shown = new Flight[Level1Map.StandCount];
        // A stand only accepts dock work once its aircraft has landed and
        // taxied in; taxiing aircraft are still physical obstacles.
        // 机位就绪状态在 ShiftSim（standReady），此处仅保留滑行动画。
        readonly List<PlaneMotion> motions = new List<PlaneMotion>();
        /// <summary>机位是否已停稳可作业（滑入完成）。供 HUD 与编辑器工具读取。</summary>
        public bool StandReady(int stand) { return Shift != null && Shift.StandReady(stand); }
        readonly List<ReplayFrame> replay = new List<ReplayFrame>();
        readonly SortedDictionary<int,ReplayFrame> networkReplayFrames = new SortedDictionary<int,ReplayFrame>();
        float recordAt, replayTime; int replayIndex, replayLastEventSeq; bool wasFinished;
        AudioSource audioSource; AudioClip bell;
        readonly CrewInput[] injected = new CrewInput[2];
        // 按玩家注入：触屏可只管 P1，P2 继续用键盘（同机双人）。
        readonly bool[] useInjected = new bool[2];
        const float ReplayNoticeDuration = 4f;
        const float FuelValveRadius = 1.2f;
        const float FuelTruckRadius = 1.9f;
        const float FuelStationTruckRange = 3.4f;
        const float FuelDockTruckRadius = 1.8f;
        const float SpillCleanReach = .45f;
        Transform fuelNozzleVisual;
        Transform fuelHoseVisual;
        LineRenderer fuelHoseLine;
        Material fuelHoseMaterial;
        readonly Dictionary<int,Transform> spillVisuals = new Dictionary<int,Transform>();
        public static Vector3 Station(ServiceKind k) { return Level1Map.Station(k); }
        public static Vector3 Dock(int stand) { return Level1Map.Dock(stand); }
        public float ReplayFraction { get { return replay.Count < 2 ? 1 : Mathf.Clamp01(replayTime / replay[replay.Count-1].Time); } }
        public float FuelTruckTankDisplay
        {
            get
            {
                if(Sim!=null && Sim.Finished && replay.Count>0 && replayIndex>=0 && replayIndex<replay.Count && replay[replayIndex].FuelShift!=null)
                    return replay[replayIndex].FuelShift.FuelTruckTank;
                return Shift==null?0f:Shift.FuelTruckTank;
            }
        }
        public void SetInput(int player, CrewInput input)
        {
            if(NetworkShift && player==LocalSeat)player=0;
            if(player<0 || player>=injected.Length)throw new ArgumentOutOfRangeException("player");
            if(Paused || Help || MatchFinished)return;
            if(Remote)
            {
                if(player!=0)return;
                injected[0]=input;useInjected[0]=true;return;
            }
            if(!Started || Sim==null || Sim.Finished)return;
            injected[player]=input;useInjected[player]=true;
        }
        void ClearInput(){Array.Clear(useInjected,0,useInjected.Length);Array.Clear(injected,0,injected.Length);}

        void ClearReplayNotice()
        {
            ReplayNotice = string.Empty;
            ReplayNoticeRemaining = 0f;
            ReplayNoticeCount = 0;
        }

        void AdvanceReplayNotice(float dt)
        {
            if(ReplayNoticeRemaining<=0f || dt<=0f || float.IsNaN(dt) || float.IsInfinity(dt))return;
            ReplayNoticeRemaining = Mathf.Max(0f,ReplayNoticeRemaining-dt);
            if(ReplayNoticeRemaining<=0f)ReplayNotice = string.Empty;
        }

        void ShowReplayNotice(string text)
        {
            if(string.IsNullOrEmpty(text))return;
            ReplayNotice = text;
            ReplayNoticeRemaining = ReplayNoticeDuration;
            ReplayNoticeCount++;
        }

        static ShiftSim BuildShift(AirportSimulation simulation = null)
        {
            // 漫油油渍生成原点：油站正面、紧邻西侧 Spine 车道的路面（车辆经过打滑区）。
            Vector3 station = Station(ServiceKind.Fuel);
            var shift = new ShiftSim(simulation ?? new AirportSimulation(), BuildPavementLegLengths(), station.x + 1.9f, station.z);
            // M3.3r 机位满溢漫油落在该机位油车停车点（油车/徒步经过同样打滑）。
            for (int s = 0; s < Level1Map.StandCount; s++)
            {
                Vector3 dock = Dock(s);
                shift.SetDockSpillOrigin(s, dock.x, dock.z);
            }
            return shift;
        }

        void Awake()
        {
            Application.targetFrameRate = 60;
            shift = BuildShift();
            AirportWorld.SetupLighting();
            AirportWorld.Build();
            var cam = new GameObject("Airport camera"); View = cam.AddComponent<Camera>();
            Level1Map.ConfigureCamera(View,false);
            View.backgroundColor = AirportStyle.Sky; View.nearClipPlane = .1f; View.farClipPlane = 100;
            View.gameObject.AddComponent<AudioListener>();
            audioSource = gameObject.AddComponent<AudioSource>();
            bell = AudioClip.Create("Service bell", 11025, 1, 22050, false);
            var samples = new float[11025]; for(int i=0;i<samples.Length;i++) samples[i] = Mathf.Sin(i * 880 * 2 * Mathf.PI / 22050) * Mathf.Exp(-i/1800f) * .12f;
            bell.SetData(samples,0);
            gameObject.AddComponent<AirportHudCanvas>();
            ResetActors();
        }
        void Start()
        {
            AppState state=AppState.Ensure();
            AppState.GameMode mode=state.Launch.Mode;
            LaunchRoomName=state.Launch.RoomName;
            switch(mode)
            {
                case AppState.GameMode.LocalCoop: StartShift(false); break;
                case AppState.GameMode.HostSandbox: StartSandbox(); break;
                case AppState.GameMode.ClientSandbox: StartRemote(); break;
                case AppState.GameMode.HostShift: StartNetworkShift(); break;
                case AppState.GameMode.ClientShift: StartNetworkShift(); break;
                default: StartShift(true); break;
            }
            state.Launch.Reset();
            BindNetSession();
        }
        void BindNetSession()
        {
            AppState state=AppState.Ensure();
            RoomManager room=state.Room;
            bool networkHost=(Sandbox || NetworkShift) && room!=null && room.IsHost && room.Phase!=RoomPhase.Idle;
            bool networkClient=Remote && room!=null && !room.IsHost && room.Phase!=RoomPhase.Idle;
            netSession=networkHost || networkClient ? new NetSession(this,room) : null;
        }
        public void Notify(string text) { Toast = text; ToastTime = 4; }
        public void StartShift(bool solo)
        {
            StartShiftWithSeat(solo, 0);
        }
        void StartShiftWithSeat(bool solo, int profileSeat)
        {
            ResetSettlement();
            ClearInput(); NetworkShift=false; localMatchId=Guid.NewGuid().ToString("N"); networkReplayReady=false; Started = true; Sandbox = false; Remote = false; RemoteSeat = -1; Snaps = null; Solo = solo;
            shift = BuildShift();
            Paused = false; Help = false;
            ClearReplayNotice();
            Deliveries = TrafficStops = 0; wasFinished = false; replay.Clear();networkReplayFrames.Clear(); recordAt = replayTime = 0; replayIndex = replayLastEventSeq = 0;
            Level1Map.ConfigureCamera(View,false);
            ResetActors(profileSeat); UpdateVisuals(); Record(); CaptureSettlementIdentity(); Notify("首班航班已落地！先卸到达行李，提前准备餐食和燃油。");
        }
        public void StartSandbox()
        {
            AppState state=AppState.Ensure();
            bool solo=state.Room==null || state.Room.Seats==null || state.Room.Seats.Length<2 || !state.Room.Seats[1].Occupied || state.Room.Seats[1].Bot;
            ClearInput(); NetworkShift=false; Started = true; Sandbox = true; Remote = false; RemoteSeat = -1; Snaps = null; Solo = solo;
            if(netSession!=null)netSession.ResetPendingInput();
            // M3.1 任务练习模式：Endless 不结算，航班由循环时刻表生成器驱动，四类任务完整可做。
            shift = BuildShift(new AirportSimulation(new Flight[0]) { Endless = true });
            shift.AttachScheduler(PracticeFlightScheduler.CreateDefault());
            Paused = false; Help = false;
            ClearReplayNotice();
            Deliveries = TrafficStops = 0; wasFinished = false; replay.Clear();networkReplayFrames.Clear(); recordAt = replayTime = 0; replayIndex = replayLastEventSeq = 0;
            Level1Map.ConfigureCamera(View,false);
            ResetActors(); UpdateVisuals(); Notify("任务练习已开始 · 航班将循环到场，四类地勤任务都可以做。");
        }
        public void StartRemote()
        {
            AppState state=AppState.Ensure();
            ResetSettlement();
            ClearInput(); NetworkShift=false; Started = true; Sandbox = false; Remote = true; Solo = false; Paused = false; Help = false;
            // client 持有只读镜像：逐快照整块覆盖，永不调用意图 API 与 Tick。
            shift = BuildShift(new AirportSimulation(new Flight[0]) { Endless = true });
            RemoteSeat = state.Room != null && state.Room.LocalSeat >= 0 ? state.Room.LocalSeat : 1;
            Snaps = new SnapshotBuffer();
            remoteToastSeq = int.MinValue;
            ClearReplayNotice();
            if(netSession!=null)netSession.ResetPendingInput();
            for(int i=0;i<RemoteLabels.Length;i++)RemoteLabels[i]=string.Empty;
            Deliveries = TrafficStops = 0; wasFinished = false; replay.Clear();networkReplayFrames.Clear(); recordAt = replayTime = 0; replayIndex = 0;
            Level1Map.ConfigureCamera(View,false);
            ResetActors();
            foreach(var c in Crew)if(c.Visual)c.Visual.gameObject.SetActive(false);
            foreach(var c in Carts)if(c.Visual)c.Visual.gameObject.SetActive(false);
            Notify("正在等待房间首帧状态。");
        }
        /// <summary>产品 LAN 有限班岗入口；StartSandbox 保留给 M3 无限任务练习验收。</summary>
        public void StartNetworkShift()
        {
            RoomManager room=AppState.Ensure().Room;
            if(room==null) { StartShift(true);return; }
            if(room.IsHost)
            {
                StartShiftWithSeat(room.Seats[1].Bot || !room.Seats[1].Occupied, Math.Max(0,room.LocalSeat));
                NetworkShift=true;
                if(room!=null) for(int i=0;i<Crew.Count;i++) Crew[i].Bot=room.Seats[i].Bot;
                replay.Clear();networkReplayFrames.Clear();
            }
            else StartNetworkRemote();
            CaptureSettlementIdentity();
            MigrationStatus=string.Empty;
            networkReplayReady=false;
            ReplayStatus="正在记录共同班岗";
            BindNetSession();
        }
        public void StartNetworkRemote()
        {
            StartRemote(); NetworkShift=true;
            shift=BuildShift(new AirportSimulation(new Flight[0]));
            CaptureSettlementIdentity();
            ReplayStatus="正在同步共同班岗";
            networkReplayReady=false;
            BindNetSession();
        }
        public void OnNetworkRetry()
        {
            NetSession existing=netSession;
            StartNetworkShift();
            if(existing!=null) { netSession=existing; existing.ResetRound(); }
        }
        public void PromoteAuthority(Snapshot checkpoint)
        {
            if(checkpoint==null) { MigrationStatus="没有可恢复的班岗检查点"; return; }
            ClearInput();
            Remote=false; Sandbox=false; NetworkShift=true; Solo=false;
            RemoteSeat=-1; Snaps=null;
            ApplyRemoteSnapshot(checkpoint,checkpoint,1f);
            Shift.RestoreCheckpoint(checkpoint.Shift,checkpoint.Flights,checkpoint.Score,checkpoint.Elapsed,
                checkpoint.Ended,checkpoint.CompletedFlights,checkpoint.MissedFlights,checkpoint.CompletedTasks);
            for(int i=0;i<Crew.Count;i++) { Crew[i].Bot=!AppState.Ensure().Room.Seats[i].Occupied || AppState.Ensure().Room.Seats[i].Bot; Shift.SetHumanSeat(i,!Crew[i].Bot); }
            Deliveries=checkpoint.Deliveries; TrafficStops=checkpoint.TrafficStops;
            restoringPlanes=true;
            for(int i=0;i<planes.Length;i++) { if(planes[i])Destroy(planes[i].gameObject); planes[i]=null;shown[i]=null; }
            motions.Clear(); UpdateVisuals(); restoringPlanes=false;
            MigrationStatus="已接管房间 · 原席位与班岗进度已恢复";
            Notify(MigrationStatus);
            if(MatchFinished) Finish();
        }

        void ResetActors(int profileSeat = -1)
        {
            ClearFuelVisuals();
            foreach(var c in Crew) if(c.Visual) { c.Visual.gameObject.SetActive(false); Destroy(c.Visual.gameObject); }
            foreach(var c in Carts) if(c.Visual) { c.Visual.gameObject.SetActive(false); Destroy(c.Visual.gameObject); }
            Crew.Clear(); Carts.Clear();
            foreach(var person in replayPeople)if(person)Destroy(person.gameObject);replayPeople.Clear();
            ClearPassengerVisuals();
            AppState state=AppState.Ensure();
            PlayerName=state.Profile.Name;
            int localSeat=profileSeat >= 0 ? profileSeat : LocalSeat;
            for(int i=0;i<2;i++)
            {
                if(planes[i]) Destroy(planes[i].gameObject); shown[i] = null;
                Color color=i==0?AirportStyle.Player1:AirportStyle.Player2;
                string name=i==0?"玩家 1":"玩家 2";
                if(i==localSeat){color=CabinWorld.PlayerColor(state.Profile.ColorIndex);name=state.Profile.Name;}
                else if(state.Room!=null && state.Room.Seats!=null && i<state.Room.Seats.Length && !string.IsNullOrEmpty(state.Room.Seats[i].Name))name=state.Room.Seats[i].Name;
                var c = new Crew {Index=i, Position=Level1Map.CrewSpawn(i), Color=color, Name=name, Bot=i==1&&Solo};
                c.Visual = AirportWorld.CreateCrew(c.Name,c.Color,c.Position); Crew.Add(c);
            }
            for(int s=2;s<planes.Length;s++){ if(planes[s]) Destroy(planes[s].gameObject); shown[s]=null; }
            if(Shift!=null) for(int s=0;s<planes.Length;s++) Shift.SetStandReady(s,true);
            foreach(ServiceKind k in new[]{ServiceKind.Meals, ServiceKind.Baggage, ServiceKind.Fuel})
            {
                var c = new Cart {Kind=k, Position=Level1Map.CartPark(k)};
                c.Visual = AirportWorld.CreateCart(k, k.ToString(), c.Position);
                c.Cargo = AirportWorld.CreateCargo(k, c.Visual); Carts.Add(c);
            }
            if(Shift!=null && Carts.Count>(int)ServiceKind.Fuel)
                Shift.SetFuelTruckAtStation(Near(Carts[(int)ServiceKind.Fuel].Position,Station(ServiceKind.Fuel),FuelStationTruckRange));
            SyncFuelEquipmentVisuals();
        }

        void ClearFuelVisuals()
        {
            if(fuelNozzleVisual)Destroy(fuelNozzleVisual.gameObject);
            fuelNozzleVisual=null;
            if(fuelHoseVisual)Destroy(fuelHoseVisual.gameObject);
            fuelHoseVisual=null;
            if(fuelHoseLine)Destroy(fuelHoseLine.gameObject);
            fuelHoseLine=null;
            if(fuelHoseMaterial)Destroy(fuelHoseMaterial);
            fuelHoseMaterial=null;
            foreach(var pair in spillVisuals)if(pair.Value)Destroy(pair.Value.gameObject);
            spillVisuals.Clear();
        }

        void SyncFuelEquipmentVisuals()
        {
            SyncFuelEquipmentVisuals(null);
        }

        Vector3 HeldItemPosition(int seat,ShiftSnap snapshot,Vector3 fallback)
        {
            if(seat<0 || seat>=Crew.Count)return fallback;
            Crew holder=Crew[seat];
            Vector3 forward=holder.Visual?holder.Visual.forward:Vector3.forward;
            Vector3 holderPosition=snapshot!=null&&holder.Visual?holder.Visual.position:holder.Position;
            return holderPosition+Vector3.up*.92f+forward*.34f+Vector3.right*.22f;
        }

        void SyncFuelEquipmentVisuals(ShiftSnap snapshot, Flight[] replayFlights = null)
        {
            if(Shift==null || Carts.Count<=(int)ServiceKind.Fuel)return;
            if(!fuelNozzleVisual)
            {
                var nozzle=GameObject.CreatePrimitive(PrimitiveType.Capsule);
                nozzle.name="Fuel nozzle";
                var collider=nozzle.GetComponent<Collider>();if(collider)Destroy(collider);
                nozzle.transform.localScale=new Vector3(.12f,.36f,.12f);
                nozzle.GetComponent<Renderer>().material.color=AirportStyle.Fuel;
                fuelNozzleVisual=nozzle.transform;
            }
            if(!fuelHoseVisual)
            {
                var hose=GameObject.CreatePrimitive(PrimitiveType.Capsule);
                hose.name="Fuel truck hose";
                var collider=hose.GetComponent<Collider>();if(collider)Destroy(collider);
                hose.transform.localScale=new Vector3(.11f,.3f,.11f);
                hose.GetComponent<Renderer>().material.color=AirportWorld.Hex("D8DFD9");
                fuelHoseVisual=hose.transform;
                var line=new GameObject("Fuel truck hose line");
                fuelHoseLine=line.AddComponent<LineRenderer>();
                fuelHoseMaterial=new Material(Shader.Find("Sprites/Default"));
                fuelHoseLine.sharedMaterial=fuelHoseMaterial;
                fuelHoseLine.startWidth=.06f;fuelHoseLine.endWidth=.06f;
                fuelHoseLine.startColor=AirportWorld.Hex("2E3532");fuelHoseLine.endColor=AirportWorld.Hex("2E3532");
                fuelHoseLine.useWorldSpace=true;fuelHoseLine.positionCount=2;
            }

            Cart fuelCart=Carts[(int)ServiceKind.Fuel];
            Vector3 cartPosition=snapshot!=null&&fuelCart.Visual?fuelCart.Visual.position:fuelCart.Position;
            Vector3 cartForward=fuelCart.Visual?fuelCart.Visual.forward:Vector3.forward;
            Vector3 cartRight=fuelCart.Visual?fuelCart.Visual.right:Vector3.right;

            // 站内油枪：枪架 / 手持 / 插在站边油车上（M3.3r 永不随车离站）。
            int nozzleState=snapshot==null?(int)Shift.NozzleState:snapshot.NozzleState;
            int nozzleSeat=snapshot==null?Shift.NozzleSeat:snapshot.NozzleSeat;
            Vector3 nozzlePosition=Station(ServiceKind.Fuel)+new Vector3(.35f,.48f,.15f);
            switch((NozzlePhase)nozzleState)
            {
                case NozzlePhase.Held:
                    nozzlePosition=HeldItemPosition(nozzleSeat,snapshot,nozzlePosition);
                    break;
                case NozzlePhase.OnTruck:
                    nozzlePosition=cartPosition+Vector3.up*.92f+cartForward*.28f+cartRight*.32f;
                    break;
            }
            fuelNozzleVisual.position=nozzlePosition;
            fuelNozzleVisual.rotation=Quaternion.Euler(0,0,90f);
            fuelNozzleVisual.gameObject.SetActive(true);

            // 车载油管：收在车上时只显示车模自带的卷盘；手持/接机时显示管头与连到油车的软管。
            int hoseState=snapshot==null?(int)Shift.HoseState:snapshot.HoseState;
            int hoseSeat=snapshot==null?Shift.HoseSeat:snapshot.HoseSeat;
            string hoseFlightId=snapshot==null?Shift.HoseFlightId:(snapshot.HoseFlightId??string.Empty);
            Vector3 reel=cartPosition+Vector3.up*.78f-cartForward*.85f+cartRight*.49f;
            Vector3 hosePosition=reel;
            bool showHose=false;
            switch((HosePhase)hoseState)
            {
                case HosePhase.Held:
                    hosePosition=HeldItemPosition(hoseSeat,snapshot,reel);showHose=true;
                    break;
                case HosePhase.OnAircraft:
                    Flight flight=null;
                    if(snapshot==null)flight=Shift.FindFlight(hoseFlightId);
                    else if(replayFlights!=null)
                        for(int i=0;i<replayFlights.Length;i++)
                            if(replayFlights[i]!=null&&replayFlights[i].Id==hoseFlightId){flight=replayFlights[i];break;}
                    if(flight!=null && flight.Stand>=0)
                        hosePosition=Dock(flight.Stand)+Vector3.up*.55f+Vector3.right*.6f;
                    showHose=true;
                    break;
            }
            fuelHoseVisual.position=hosePosition;
            fuelHoseVisual.rotation=Quaternion.Euler(0,0,90f);
            fuelHoseVisual.gameObject.SetActive(showHose);
            fuelHoseLine.gameObject.SetActive(showHose);
            if(showHose){fuelHoseLine.SetPosition(0,reel);fuelHoseLine.SetPosition(1,hosePosition);}

            var live=new HashSet<int>();
            if(snapshot==null)
            {
                for(int i=0;i<Shift.Spills.Count;i++)
                {
                    ShiftSpill spill=Shift.Spills[i];if(spill==null)continue;
                    SyncSpillVisual(spill.Id,spill.X,spill.Z,spill.Radius,spill.CleanWork);
                    live.Add(spill.Id);
                }
            }
            else if(snapshot.Spills!=null)
            {
                for(int i=0;i<snapshot.Spills.Length;i++)
                {
                    SpillSnap spill=snapshot.Spills[i];if(spill==null)continue;
                    SyncSpillVisual(spill.Id,spill.X,spill.Z,spill.Radius,spill.CleanWork);
                    live.Add(spill.Id);
                }
            }
            if(spillVisuals.Count>0)
            {
                var stale=new List<int>();
                foreach(var pair in spillVisuals)if(!live.Contains(pair.Key))stale.Add(pair.Key);
                for(int i=0;i<stale.Count;i++)
                {
                    Transform visual=spillVisuals[stale[i]];if(visual) { visual.gameObject.SetActive(false); Destroy(visual.gameObject); } spillVisuals.Remove(stale[i]);
                }
            }
        }

        void SyncSpillVisual(int id,float x,float z,float spillRadius,float cleanWork)
        {
            Transform visual;
            if(!spillVisuals.TryGetValue(id,out visual) || !visual)
            {
                GameObject puddle=GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                puddle.name="Fuel spill "+id;
                var collider=puddle.GetComponent<Collider>();if(collider)Destroy(collider);
                visual=puddle.transform;spillVisuals[id]=visual;
            }
            visual.position=new Vector3(x,.025f,z);
            float radius=spillRadius>0f?spillRadius:ShiftSim.SpillRadius;
            visual.localScale=new Vector3(radius*2f,.022f,radius*2f);
            visual.GetComponent<Renderer>().material.color=cleanWork>0f?new Color(.27f,.22f,.12f):new Color(.065f,.07f,.065f);
            visual.gameObject.SetActive(true);
        }
        // 任务语义色统一走 AirportStyle（燃油绿、登机青蓝，对齐 v7）。
        public static Color ColorFor(ServiceKind k) { return AirportStyle.ServiceColor(k); }
        /// <summary>车辆任务域状态（cartId = Carts 下标 = ServiceKind 下标）。</summary>
        public ShiftCart CartState(Cart cart) { int i=Carts.IndexOf(cart); return i<0 || Shift==null || i>=Shift.Carts.Length ? null : Shift.Carts[i]; }
        /// <summary>该席位当前所持车辆的交付读条（HUD 世界提示条）。</summary>
        public float DeliverWorkOf(Crew c) { var s=c==null?null:CartState(c.Cart); return s==null?0:s.DeliverWork; }
        public Flight SelectedFlight(Crew c)
        {
            if(c==null || Sim==null)return null;
            if(c.Selected >= 0 && c.Selected < Sim.Flights.Count && Sim.Flights[c.Selected].Status == FlightStatus.Servicing) return Sim.Flights[c.Selected];
            foreach(var f in Sim.Flights) if(f.Status==FlightStatus.Servicing) { if(!Remote)c.Selected = Sim.Flights.IndexOf(f); return f; }
            return null;
        }
        CrewInput ReadInput(int i)
        {
            CrewInput merged = i==0 ? new CrewInput { Move=new Vector2((Input.GetKey(KeyCode.D)?1:0)-(Input.GetKey(KeyCode.A)?1:0),(Input.GetKey(KeyCode.W)?1:0)-(Input.GetKey(KeyCode.S)?1:0)),Pressed=Input.GetKeyDown(KeyCode.E),Held=Input.GetKey(KeyCode.E),Cycle=Input.GetKeyDown(KeyCode.Q)}
                : new CrewInput {Move=new Vector2((Input.GetKey(KeyCode.RightArrow)?1:0)-(Input.GetKey(KeyCode.LeftArrow)?1:0),(Input.GetKey(KeyCode.UpArrow)?1:0)-(Input.GetKey(KeyCode.DownArrow)?1:0)),Pressed=Input.GetKeyDown(KeyCode.RightShift),Held=Input.GetKey(KeyCode.RightShift),Cycle=Input.GetKeyDown(KeyCode.Return)};
            // 注入输入与键盘按位合并：移动幅值>0.01 时覆盖键盘方向，
            // 按下/按住/切换取 OR——触屏 P1 与键盘 P2 可同时操作。
            if(!useInjected[i]) return merged;
            CrewInput touch=injected[i];
            if(touch.Move.sqrMagnitude>.0001f) merged.Move=touch.Move;
            merged.Pressed|=touch.Pressed; merged.Held|=touch.Held; merged.Cycle|=touch.Cycle;
            return merged;
        }
        CrewInput InputFor(int i,float dt)
        {
            if((Sandbox || NetworkShift) && netSession!=null && netSession.SeatIsBot(i))Crew[i].Bot=true;
            if(Crew[i].Bot)return BotInput(Crew[i],Mathf.Min(dt,.1f));
            if((Sandbox || NetworkShift) && netSession!=null && netSession.ControlsRemoteSeat(i))return netSession.InputFor(i);
            return ReadInput(NetworkShift && i==LocalSeat ? 0 : i);
        }
        // 外部接管（M1 截图/回放工具）：true 时 Update 不驱动，由调用方以固定步长调 Step。
        public bool ExternalControl;
        void Update()
        {
            ToastTime -= Time.unscaledDeltaTime;
            if(ExternalControl) return;
            if(Input.GetKeyDown(KeyCode.F1)) Help = !Help;
            if(Input.GetKeyDown(KeyCode.Escape) && Started && (Sim==null || !Sim.Finished)) Paused = !Paused;
            Step(Time.deltaTime);
            PumpReplayExport();
        }
        /// <summary>单步推进一局。Update 以真实 dt 调用；编辑器工具用固定步长保证确定性。</summary>
        public void Step(float dt)
        {
            if(dt<=0f || float.IsNaN(dt) || float.IsInfinity(dt))return;
            ObserveSettlementIdentity();
            AdvanceReplayNotice(dt);
            if((Sandbox || Remote || NetworkShift) && netSession!=null && !netSession.Pump(dt))return;
            if(Remote) {StepRemote(dt);return;}
            if(!Started || Sim==null) {ClearInput();return;}
            if(Sim.Finished)
            {
                ClearInput();
                if(NetworkShift && netSession!=null)netSession.StepHost(dt);
                PlayReplay(dt);return;
            }
            if(Paused || Help)
            {
                ClearInput();
                if(netSession!=null)
                {
                    for(int i=0;i<Crew.Count;i++)if(netSession.ControlsRemoteSeat(i))netSession.InputFor(i);
                    if(NetworkShift)netSession.StepHost(dt);
                }
                return;
            }
            UpdateFuelTruckStationAccess();
            CrewInput[] inputs=new CrewInput[Crew.Count];
            for(int i=0;i<Crew.Count;i++) { inputs[i]=InputFor(i,dt); Shift.SetHumanSeat(i,!Crew[i].Bot); }
            float advanced=0f;
            Shift.Tick(dt,delegate(float slice)
            {
                advanced+=slice;
                for(int i=0;i<Crew.Count;i++)
                {
                    Crew c=Crew[i];CrewInput input=inputs[i];
                    if(input.Cycle)for(int j=1;j<=Sim.Flights.Count;j++) { int n=(c.Selected+j)%Sim.Flights.Count;if(Sim.Flights[n].Status==FlightStatus.Servicing){c.Selected=n;break;} }
                    Move(c,input.Move,Mathf.Min(slice,.1f));Interact(c,input,slice);
                    inputs[i].Pressed=false;inputs[i].Cycle=false;
                }
                Shift.EndFrame();
            });
            ClearInput();
            SyncPassengerVisuals();DrainShiftEvents();UpdatePlaneMotions(advanced);UpdateVisuals();
            if(!Sandbox && !NetworkShift && Sim.Elapsed>=recordAt) {Record();recordAt=Sim.Elapsed+.25f;}
            if(Sandbox || NetworkShift)StepSandboxHost(dt);
            if(Sim.Finished)Finish();
        }
        /// <summary>ShiftSim 事件 → 现有 toast/HUD/音效通道（host 与本地共用）。</summary>
        void DrainShiftEvents()
        {
            if(Shift==null)return;
            List<ShiftEvent> events=Shift.DrainEvents();
            for(int i=0;i<events.Count;i++)
            {
                ShiftEvent e=events[i];
                if(e.Type==ShiftEventTypes.Delivered)Deliveries++;
                else if(e.Type==ShiftEventTypes.GateOpened)TrafficStops++;
                if(e.Type==ShiftEventTypes.WrongDelivery || e.Type==ShiftEventTypes.TaskFailed)RecordTaskFailureEvent(e);
                if(e.Bell && audioSource!=null && bell!=null)audioSource.PlayOneShot(bell);
                if(!string.IsNullOrEmpty(e.Text))Notify(e.Text);
            }
        }
        void RecordTaskFailureEvent(ShiftEvent e)
        {
            AppState state=AppState.Ensure();
            if(state.Events==null)return;
            var parameters=new Dictionary<string,object>();
            parameters["flight_id"]=e.FlightId??string.Empty;
            parameters["related_flight_id"]=e.RelatedFlightId??string.Empty;
            parameters["kind"]=e.Kind;
            parameters["seat"]=e.Seat;
            parameters["event_seq"]=e.Sequence;
            parameters["elapsed"]=e.Time;
            state.Events.Enqueue(e.Type==ShiftEventTypes.WrongDelivery?GameEventQueue.WrongDelivery:GameEventQueue.TaskFailed,null,parameters);
        }
        void StepSandboxHost(float dt)
        {
            if(netSession!=null)netSession.StepHost(dt);
        }
        void StepRemote(float dt)
        {
            if(netSession!=null)
            {
                Snapshot a,b;float t;
                if(netSession.SampleRemote(out a,out b,out t))ApplyRemoteSnapshot(a,b,t);
                bool neutral=Paused||Help||MatchFinished;
                CrewInput input=neutral?new CrewInput():ReadInput(0);
                netSession.SendRemoteInput(dt,input,neutral);
            }
            // 只读镜像的表现层外推：飞机滑行/生成、旅客走位、货物显隐都读镜像，
            // 任务判定与阻车判定仍在 host（client 永不调意图 API/Tick）。
            if(MatchFinished) { Finish();PlayReplay(dt);ClearInput();return; }
            UpdatePlaneMotions(dt);
            UpdateVisuals();
            SyncPassengerVisuals();
            ClearInput();
        }
        void ApplyRemoteSnapshot(Snapshot a,Snapshot b,float t)
        {
            if(NetworkShift && b.Ended)t=1f;
            for(int i=0;i<Crew.Count;i++)
            {
                Crew crew=Crew[i];
                float x=Mathf.Lerp(a.Crew[i].X,b.Crew[i].X,t);
                float z=Mathf.Lerp(a.Crew[i].Z,b.Crew[i].Z,t);
                float yaw=Mathf.LerpAngle(a.Crew[i].RotY,b.Crew[i].RotY,t);
                crew.Position=new Vector3(x,crew.Position.y,z);
                // 目标航班下标（M3.1）：client 目标 chip 只读显示，判定仍在 host。
                crew.Selected=b.Crew[i].Sel;
                crew.BlockedByTraffic=b.Crew[i].BlockedByTraffic;
                if(crew.Visual)
                {
                    crew.Visual.position=crew.Position;
                    crew.Visual.rotation=Quaternion.Euler(0,yaw,0);
                    crew.Visual.gameObject.SetActive(true);
                }
            }
            for(int i=0;i<Carts.Count;i++)
            {
                Cart cart=Carts[i];
                float x=Mathf.Lerp(a.Carts[i].X,b.Carts[i].X,t);
                float z=Mathf.Lerp(a.Carts[i].Z,b.Carts[i].Z,t);
                float yaw=Mathf.LerpAngle(a.Carts[i].RotY,b.Carts[i].RotY,t);
                cart.Position=new Vector3(x,cart.Position.y,z);
                if(cart.Visual)
                {
                    cart.Visual.position=cart.Position;
                    cart.Visual.rotation=Quaternion.Euler(0,yaw,0);
                    cart.Visual.gameObject.SetActive(true);
                }
                if(cart.Cargo)cart.Cargo.gameObject.SetActive(b.Carts[i].Cargo!=0);
                cart.Owner=null;
            }
            for(int i=0;i<Crew.Count;i++)Crew[i].Cart=null;
            for(int i=0;i<Carts.Count;i++)
            {
                int owner=b.Carts[i].OwnerSeat;
                if(owner<0||owner>=Crew.Count)continue;
                Cart cart=Carts[i];Crew crew=Crew[owner];
                cart.Owner=crew;crew.Cart=cart;
            }
            for(int i=0;i<Crew.Count;i++)
            {
                int held=b.Crew[i].HeldCart;
                if(held<0||held>=Carts.Count)continue;
                Cart cart=Carts[held];Crew crew=Crew[i];
                if(cart.Owner==null){cart.Owner=crew;crew.Cart=cart;}
            }
            for(int i=0;i<Crew.Count;i++)
            {
                Crew crew=Crew[i];
                if(crew.Visual)crew.Visual.position=crew.Position+Vector3.up*(crew.Cart!=null?.35f:0f);
            }
            for(int i=0;i<RemoteLabels.Length;i++)RemoteLabels[i]=b.Labels[i]??string.Empty;
            if(b.ToastSeq!=remoteToastSeq)
            {
                remoteToastSeq=b.ToastSeq;
                if(!string.IsNullOrEmpty(b.Toast))Notify(b.Toast);
            }
            // 只读镜像整块覆盖（HUD/回放采样读镜像；client 永不调用意图 API）。
            if(Shift!=null)
            {
                if(NetworkShift)Shift.RestoreCheckpoint(b.Shift,b.Flights,b.Score,b.Elapsed,b.Ended,b.CompletedFlights,b.MissedFlights,b.CompletedTasks);
                else Shift.ApplyMirror(b.Shift,b.Flights,b.Score,b.Elapsed);
            }
            if(NetworkShift){Deliveries=b.Deliveries;TrafficStops=b.TrafficStops;}
        }
        void Move(Crew c, Vector2 input, float dt)
        {
            c.BlockedByTraffic=false;
            if(c.Cart!=null && !Shift.CanDriveCart((int)c.Cart.Kind))input=Vector2.zero;
            Vector3 dir = new Vector3(input.x,0,input.y); if(dir.sqrMagnitude>1) dir.Normalize();
            float speed=c.Cart==null?4.5f:3.6f;
            Vector3 proposed=c.Position+dir*speed*dt;
            if(c.Cart!=null)c.Cart.Slipping=false;
            if(c.Cart!=null && c.Cart.Kind==ServiceKind.Fuel)
            {
                bool slipping=Shift.SpillAt(c.Position.x,c.Position.z)||Shift.SpillAt(proposed.x,proposed.z);
                c.Cart.Slipping=slipping;
                if(slipping)speed*=.32f;
            }
            Vector3 next = c.Position + dir*speed*dt;
            next.x=Mathf.Clamp(next.x,Level1Map.WalkMin.x,Level1Map.WalkMax.x); next.z=Mathf.Clamp(next.z,Level1Map.WalkMin.y,Level1Map.WalkMax.y);
            // Walkable service lanes stay clear of each parked aircraft.
            for(int s=0;s<Level1Map.StandCount;s++)
            {
                if(Sim==null || Sim.ActiveAtStand(s)==null) continue;
                if(Shift.StandReady(s) && Level1Map.InsideParkedAircraftArea(next,s)) next=c.Position;
                // A taxiing aircraft is a moving obstacle even off its stand.
                if(planes[s]!=null && !Shift.StandReady(s) && Vector3.Distance(next,planes[s].position)<2.2f) next=c.Position;
            }
            if(c.Cart!=null)
            {
                // Vehicles must stay on the road network: spine, facility
                // stubs, ramp branches, and the ramps themselves.
                next=Level1Map.ClampToDrivable(c.Position,next);
                for(int i=0;i<Shift.Passengers.Count;i++)
                {
                    ShiftPassenger p=Shift.Passengers[i];
                    if(p.Released && PassengerBlocksSegment(c.Position,next,p)) { next=c.Position; c.BlockedByTraffic=true; break; }
                }
                foreach(var cart in Carts) if(cart!=c.Cart && Vector3.Distance(next,cart.Position)<.9f) {next=c.Position;break;}
            }
            c.Position=next;
            if(dir.sqrMagnitude>.01f) c.Visual.rotation=Quaternion.Slerp(c.Visual.rotation,Quaternion.LookRotation(dir),dt*12);
            if(c.Cart!=null) {c.Cart.Position=c.Position; c.Cart.Visual.rotation=c.Visual.rotation;}
        }
        void UpdateFuelTruckStationAccess()
        {
            if(Shift==null || Carts.Count<=(int)ServiceKind.Fuel)return;
            Cart fuel=Carts[(int)ServiceKind.Fuel];
            Shift.SetFuelTruckAtStation(Near(fuel.Position,Station(ServiceKind.Fuel),FuelStationTruckRange));
        }
        static bool Near(Vector3 a, Vector3 b,float radius=1.65f) { return Vector3.Distance(a,b)<radius; }
        Cart FuelCart { get { return Carts.Count>(int)ServiceKind.Fuel?Carts[(int)ServiceKind.Fuel]:null; } }
        bool NearFuelValve(Vector3 position) { return Near(position,Station(ServiceKind.Fuel),FuelValveRadius); }
        bool NearFuelTruck(Vector3 position) { return FuelCart!=null && Near(position,FuelCart.Position,FuelTruckRadius); }
        bool FuelTruckAtDock(int stand) { return FuelCart!=null && Near(FuelCart.Position,Dock(stand),FuelDockTruckRadius); }
        /// <summary>油车当前停靠且有航班的机位（-1 = 不在任何机位）。</summary>
        int FuelTruckDockStand()
        {
            if(FuelCart==null || Sim==null)return -1;
            for(int s=0;s<Level1Map.StandCount;s++)if(Sim.ActiveAtStand(s)!=null&&FuelTruckAtDock(s))return s;
            return -1;
        }
        bool FlightNeedsFuel(Flight flight)
        {
            return flight!=null && flight.Status==FlightStatus.Servicing && Shift.StandReady(flight.Stand) &&
                flight.Progress[(int)ServiceKind.Fuel]<1f && !Shift.IsTaskFailed(flight.Id,ServiceKind.Fuel);
        }
        string FuelNoDriveReason()
        {
            if(Shift.ValveOpen)return "阀门开着 · 先去油站关阀";
            if(Shift.NozzleState==NozzlePhase.OnTruck)return "站内油枪插在车上 · 关阀后自动归位";
            if(Shift.HoseState==HosePhase.OnAircraft)return "车载油管接在飞机上 · 点飞机断开";
            if(Shift.HoseState==HosePhase.Held)return "车载油管不在车上 · 先收回油管";
            return string.Empty;
        }
        void MountFuelTruck(Crew crew)
        {
            if(FuelCart.Owner!=null)return;
            if(!Shift.CanDriveCart((int)ServiceKind.Fuel)){Notify(FuelNoDriveReason());return;}
            if(Shift.TryClaimCart(crew.Index,(int)ServiceKind.Fuel))
            {crew.Cart=FuelCart;FuelCart.Owner=crew;crew.Position=FuelCart.Position;}
        }
        /// <summary>
        /// M3.3r 燃油"紧急"点按（优先于按住清理油渍）：开着的阀门 → 关阀；接在飞机上的油管 → 断开；
        /// 手持车载油管 → 接管 / 收回。优先级由 ShiftSim.Resolve*Tap 裁定，这里只回填空间事实。
        /// perform=false 时只给 Context/ActionLabel 计算提示，与实际分派共用同一判定。
        /// </summary>
        bool FuelUrgentAction(Crew c,out string hint,out string label,bool perform)
        {
            hint=label=string.Empty;
            if(NearFuelValve(c.Position)&&Shift.ValveOpen)
            {
                bool inserted=Shift.NozzleState==NozzlePhase.OnTruck&&Shift.FuelTruckAtStation;
                hint=inserted&&Shift.FuelTruckTank<1f?"阀门已开 · 油车加注中 · 点按关阀（油枪自动归位）":
                    inserted?(Shift.StationSpilling?"油车已满 · 正在漫油，立即关阀":"油车已满 · 2 秒内关阀，否则漫油"):
                    "阀门已开 · 燃油外溢 · 立即关阀";
                label="关阀";
                if(perform)Shift.ApplyFuelTap(c.Index,Shift.ResolveStationTap(c.Index),null,false);
                return true;
            }
            if(Shift.HoseState==HosePhase.OnAircraft)
            {
                Flight connected=Shift.FindFlight(Shift.HoseFlightId);
                bool atDock=connected!=null&&connected.Stand>=0&&Near(c.Position,Dock(connected.Stand));
                // 断开以"点飞机"为准；站在油车旁（与机位交互圈相邻）同样可断开，避免两圈之间的死角。
                if(atDock || NearFuelTruck(c.Position))
                {
                    string id=connected==null?"飞机":connected.Id;
                    bool full=connected!=null&&connected.Progress[(int)ServiceKind.Fuel]>=1f;
                    hint=full?(Shift.HoseSpilling?id+" 已加满 · 正在漫油，点按断开":id+" 已加满 · 2 秒内断开，否则漫油"):
                        Shift.FuelTruckTank<=0f?"油车空了 · 点按断开，开回油站加注":
                        "正在给 "+id+" 自动加注 "+Mathf.RoundToInt((connected==null?0f:connected.Progress[(int)ServiceKind.Fuel])*100f)+"% · 点按断开";
                    label="断开";
                    if(perform)Shift.ApplyFuelTap(c.Index,Shift.ResolveAircraftTap(c.Index,Shift.HoseFlightId,true),Shift.HoseFlightId,true);
                    return true;
                }
                return false;
            }
            if(Shift.HoseState==HosePhase.Held&&Shift.HoseSeat==c.Index)
            {
                int stand=FuelTruckDockStand();
                Flight dockFlight=stand>=0?Sim.ActiveAtStand(stand):null;
                bool atDock=stand>=0&&Near(c.Position,Dock(stand));
                if(atDock&&Shift.ResolveAircraftTap(c.Index,dockFlight.Id,true)==FuelTap.AttachHose)
                {
                    hint="点按把油管接到 "+dockFlight.Id+" · 自动加注";label="接管";
                    if(perform)Shift.AttachHose(c.Index,dockFlight.Id,true);
                    return true;
                }
                if(NearFuelTruck(c.Position))
                {
                    hint=Shift.FuelTruckTank<=0f?"油车空了 · 点按收回油管":"点按把油管收回油车";label="收回";
                    if(perform)Shift.ApplyFuelTap(c.Index,Shift.ResolveTruckTap(c.Index,null),null,false);
                    return true;
                }
                if(atDock)
                {
                    hint=Shift.FuelTruckTank<=0f?"油车没油了 · 回油车收回油管":"该航班不需要加油 · 回油车收回油管";
                    label="收回";
                    if(perform)Notify(hint);
                    return true;
                }
                return false;
            }
            return false;
        }
        /// <summary>空手站内/车边的燃油点按（拿枪、归还、开阀、插枪、拔枪、取管、上车）。</summary>
        bool FuelStationOrTruckAction(Crew c,out string hint,out string label,bool perform)
        {
            hint=label=string.Empty;
            if(NearFuelValve(c.Position))
            {
                FuelTap stationTap=Shift.ResolveStationTap(c.Index);
                switch(stationTap)
                {
                    case FuelTap.TakeNozzle:hint="拿起站内油枪 · 插到站边油车";label="拿枪";break;
                    case FuelTap.ReturnNozzle:hint="拿着油枪 · 点站边油车插枪，点油站归还";label="归还";break;
                    case FuelTap.CloseValve:hint="阀门已开 · 点按关阀";label="关阀";break;
                    default:
                        if(Shift.NozzleState==NozzlePhase.OnTruck)
                        {
                            hint=Shift.FuelTruckTank>=1f?"油车已满 · 开阀 2 秒后漫油":"油枪已插车 · 点按开阀加注";
                            label=Shift.FuelTruckTank>=1f?"开阀漫油":"开阀";
                        }
                        else {hint="搭档拿着油枪 · 开阀会漫油";label="开阀漫油";}
                        break;
                }
                if(perform)Shift.ApplyFuelTap(c.Index,stationTap,null,false);
                return true;
            }
            if(!NearFuelTruck(c.Position))return false;
            int stand=FuelTruckDockStand();
            Flight dockFlight=stand>=0?Sim.ActiveAtStand(stand):null;
            FuelTap truckTap=Shift.ResolveTruckTap(c.Index,dockFlight==null?null:dockFlight.Id);
            switch(truckTap)
            {
                case FuelTap.InsertNozzle:hint="点按把油枪插入油车";label="插枪";break;
                case FuelTap.PullNozzle:hint="油枪已插车 · 去油站开阀，或点按拔枪归位";label="拔枪";break;
                case FuelTap.TakeHose:hint="点按取出车载油管 · 接到 "+dockFlight.Id;label="取管";break;
                case FuelTap.ReturnHose:hint="点按把油管收回油车";label="收回";break;
                case FuelTap.None:hint=Shift.ValveOpen?"油枪插着且阀门开 · 去油站关阀":"等待";label="先关阀";return true;
                default:
                    if(Shift.HoseState==HosePhase.Held&&Shift.HoseSeat!=c.Index){hint="搭档拿着车载油管";label="等待";return true;}
                    if(FuelCart.Owner!=null){hint="搭档正在驾驶燃油车";label="等待";return true;}
                    if(!Shift.CanDriveCart((int)ServiceKind.Fuel)){hint=FuelNoDriveReason();label="上车";if(perform)Notify(hint);return true;}
                    hint=Shift.FuelTruckTank<=0f&&!Shift.FuelTruckAtStation?"油车空了 · 上车开回油站加注":"驾驶燃油车";label="上车";
                    if(perform)MountFuelTruck(c);
                    return true;
            }
            if(perform)Shift.ApplyFuelTap(c.Index,truckTap,dockFlight==null?null:dockFlight.Id,stand>=0);
            return true;
        }
        int NearbySpill(Crew crew)
        {
            if(crew==null)return -1;
            float best=float.MaxValue;int found=-1;
            for(int i=0;i<Shift.Spills.Count;i++)
            {
                ShiftSpill spill=Shift.Spills[i];if(spill==null)continue;
                float distance=Vector2.Distance(new Vector2(crew.Position.x,crew.Position.z),new Vector2(spill.X,spill.Z));
                float reach=(spill.Radius>0f?spill.Radius:ShiftSim.SpillRadius)+SpillCleanReach;
                if(distance<=reach && distance<best){best=distance;found=spill.Id;}
            }
            return found;
        }
        public string Context(Crew c)
        {
            if(c==null)return string.Empty;
            if(Remote)return "正在同步搭档状态";
            if(Sim==null)return string.Empty;
            Flight selected=SelectedFlight(c);
            if(c.BlockedByTraffic)return "旅客优先通行 · 稍等或绕行";
            if(c.Cart!=null)
            {
                Cart cart=c.Cart;var state=CartState(cart);
                if(cart.Kind==ServiceKind.Fuel)
                {
                    if(!Shift.CanDriveCart((int)ServiceKind.Fuel))return "油管或阀门仍连接 · 点按下车后徒步操作";
                    if(cart.Slipping)return "路面油滑 · 油车减速中";
                    for(int s=0;s<Level1Map.StandCount;s++)if(Near(c.Position,Dock(s))&&Sim.ActiveAtStand(s)!=null)return "油车已到机位 · 点按停车";
                    return "驾驶油车 · 将燃油送到目标机位";
                }
                if(Near(c.Position,Station(cart.Kind))) return state.Arrival?"交还到达行李":state.Loaded?"货物已装好 · 出发送往 "+(string.IsNullOrEmpty(state.CargoFlightId)?"飞机":state.CargoFlightId):cart.Kind==ServiceKind.Meals?(Shift.Meal==MealPhase.Ready?"装载餐食":Shift.Meal==MealPhase.Ordered?"餐食制作中…":"下单制作餐食"):"装载 "+(selected==null?"待到达航班":selected.Id)+" 行李";
                for(int s=0;s<Level1Map.StandCount;s++) if(Near(c.Position,Dock(s)) && Sim.ActiveAtStand(s)!=null) return !Shift.StandReady(s)?"飞机正在滑入机位 · 停稳后再作业":state.Arrival?"到达行李 · 送回左侧行李站":state.Loaded?"按住交付 "+Names[(int)cart.Kind]:cart.Kind==ServiceKind.Baggage&&!Sim.ActiveAtStand(s).ArrivalBagsReturned?"取走到达行李":"空车 · 返回补给站";
                return "停下并点按松开车辆";
            }
            string fuelHint,fuelLabel;
            if(FuelUrgentAction(c,out fuelHint,out fuelLabel,false))return fuelHint;
            int spillId=NearbySpill(c);
            if(spillId>=0)
            {
                for(int i=0;i<Shift.Spills.Count;i++)if(Shift.Spills[i].Id==spillId)return "油渍打滑区 · 按住清理 "+Mathf.RoundToInt(Shift.Spills[i].CleanWork*100f)+"%";
            }
            for(int s=0;s<Level1Map.StandCount;s++)
            {
                Flight f=Sim.ActiveAtStand(s);if(f==null||!Near(c.Position,Dock(s)))continue;
                if(!Shift.StandReady(s))return "飞机正在滑入机位 · 停稳后再作业";
            }
            if(FuelStationOrTruckAction(c,out fuelHint,out fuelLabel,false))return fuelHint;
            if(Near(c.Position,Station(ServiceKind.Meals)) && Shift.Meal==MealPhase.Idle)return "下单制作餐食";
            foreach(var cart in Carts) if(cart.Kind!=ServiceKind.Fuel && cart.Owner==null && Near(c.Position,cart.Position,1.9f)) return "驾驶"+Names[(int)cart.Kind]+"车";
            if(Near(c.Position,Station(ServiceKind.Meals))) return Shift.Meal==MealPhase.Ready?"餐食已备好 · 需要餐车":Shift.Meal==MealPhase.Ordered?"餐食制作中…":"下单制作餐食";
            if(Near(c.Position,Station(ServiceKind.Boarding)))
                return selected==null?"等待航班到达":Shift.IsBoarding(selected.Id)?"关闭 "+selected.Id+" 登机口 · 已出发旅客继续前行":selected.Progress[3]>=1f?selected.Id+" 登机已完成":selected.Progress[0]<1f||selected.Progress[2]<1f?"先完成 "+selected.Id+" 餐食与燃油，再放行旅客":"放行 "+selected.Id+" 旅客 · 关闭后保留登机进度";
            return "靠近车辆或站点";
        }
        public static readonly string[] Names= ShiftSim.TaskNames;

        /// <summary>右下交互大圆钮的短动词标签，与 Context 提示共用同一套分支判断。</summary>
        public string ActionLabel(Crew c)
        {
            if(c==null)return "";
            if(Remote)return RemoteSeat>=0 && RemoteSeat<RemoteLabels.Length?RemoteLabels[RemoteSeat]:string.Empty;
            if(Sim==null)return "";
            if(c.BlockedByTraffic)return "等待";
            if(c.Cart!=null)
            {
                if(c.Cart.Kind==ServiceKind.Fuel)return Shift.CanDriveCart((int)ServiceKind.Fuel)?(c.Cart.Slipping?"慢行":"下车"):"下车";
                var state=CartState(c.Cart);
                if(Near(c.Position,Station(c.Cart.Kind)))return state.Arrival?"交还行李":state.Loaded?"放车":c.Cart.Kind==ServiceKind.Meals?(Shift.Meal==MealPhase.Ready?"装车":Shift.Meal==MealPhase.Ordered?"制作中":"下单"):"装车";
                for(int s=0;s<Level1Map.StandCount;s++)if(Near(c.Position,Dock(s))&&Sim.ActiveAtStand(s)!=null)return !Shift.StandReady(s)?"等待":state.Arrival?"送回行李站":state.Loaded?"按住交付":c.Cart.Kind==ServiceKind.Baggage&&!Sim.ActiveAtStand(s).ArrivalBagsReturned?"取行李":"放车";
                return "放车";
            }
            string fuelHint,fuelLabel;
            if(FuelUrgentAction(c,out fuelHint,out fuelLabel,false))return fuelLabel;
            int spillId=NearbySpill(c);if(spillId>=0)return "按住清理";
            for(int s=0;s<Level1Map.StandCount;s++)
            {
                Flight f=Sim.ActiveAtStand(s);if(f==null||!Near(c.Position,Dock(s)))continue;
                if(!Shift.StandReady(s))return "等待";
            }
            if(FuelStationOrTruckAction(c,out fuelHint,out fuelLabel,false))return fuelLabel;
            if(Near(c.Position,Station(ServiceKind.Meals)) && Shift.Meal==MealPhase.Idle) return "下单";
            foreach(var cart in Carts)if(cart.Kind!=ServiceKind.Fuel&&cart.Owner==null&&Near(c.Position,cart.Position,1.9f))return "上车";
            if(Near(c.Position,Station(ServiceKind.Meals)))return Shift.Meal==MealPhase.Ready?"需餐车":Shift.Meal==MealPhase.Ordered?"制作中":"下单";
            if(Near(c.Position,Station(ServiceKind.Boarding)))
            {
                Flight boardingFlight=SelectedFlight(c);
                return boardingFlight!=null&&Shift.IsBoarding(boardingFlight.Id)?"关登机口":"放行";
            }
            return "交互";
        }
        /// <summary>空间情境在表现层路由，任务/占用/油量裁定全部委托 ShiftSim；本地与 host 共用入口。</summary>
        void Interact(Crew c,CrewInput input,float dt)
        {
            if(Remote || Sim==null)return;
            c.Hint=Context(c);
            if(c.Cart!=null)
            {
                if(c.Cart.Kind==ServiceKind.Fuel)
                {
                    if(input.Pressed)Release(c);
                    return; // 驾驶位只行驶/停车，绝不操作枪、阀门或清理油渍。
                }
                if(!input.Pressed&&!input.Held)return;
                Flight carriedTarget=SelectedFlight(c);int carriedId=Carts.IndexOf(c.Cart);ShiftCart carriedState=Shift.Carts[carriedId];
                if(Near(c.Position,Station(c.Cart.Kind)))
                {
                    if(carriedState.Arrival){if(input.Pressed)Shift.ReturnArrivalBagsFromCart(c.Index,carriedId);return;}
                    if(carriedState.Loaded){if(input.Pressed)Release(c);return;}
                    if(c.Cart.Kind==ServiceKind.Meals&&input.Pressed){if(Shift.Meal==MealPhase.Ready&&carriedTarget!=null)Shift.TakeMeal(c.Index,carriedId,carriedTarget.Id);else Shift.OrderMeal(c.Index);}
                    if(c.Cart.Kind==ServiceKind.Baggage&&input.Pressed&&carriedTarget!=null)Shift.LoadCart(c.Index,carriedId,carriedTarget.Id);
                    return;
                }
                for(int s=0;s<Level1Map.StandCount;s++)
                {
                    Flight dockFlight=Sim.ActiveAtStand(s);if(dockFlight==null||!Near(c.Position,Dock(s)))continue;
                    if(!Shift.StandReady(s)){if(input.Pressed)Notify(dockFlight.Id+" 正在滑入机位 · 停稳后再作业");return;}
                    if(!carriedState.Loaded)
                    {
                        if(c.Cart.Kind==ServiceKind.Baggage&&!dockFlight.ArrivalBagsReturned&&input.Pressed)Shift.PickupArrivalBags(c.Index,carriedId,dockFlight.Id);
                        else if(input.Pressed)Release(c);
                        return;
                    }
                    Shift.Deliver(c.Index,carriedId,dockFlight.Id,input.Pressed,dt);return;
                }
                if(input.Pressed)Release(c);
                return;
            }

            Flight selected=SelectedFlight(c);
            string fuelHint,fuelLabel;
            // M3.3r：燃油全部为单次点按。关阀、断管、接管/收回优先于按住清理油渍（油渍可能就在脚下）。
            if(input.Pressed&&FuelUrgentAction(c,out fuelHint,out fuelLabel,true))return;

            if(input.Held)
            {
                int spillId=NearbySpill(c);
                if(spillId>=0){Shift.CleanSpill(c.Index,spillId,dt);return;}
            }
            if(!input.Pressed)return;

            for(int s=0;s<Level1Map.StandCount;s++)
            {
                Flight f=Sim.ActiveAtStand(s);if(f==null||!Near(c.Position,Dock(s)))continue;
                if(!Shift.StandReady(s)){Notify(f.Id+" 正在滑入机位 · 停稳后再作业");return;}
            }
            if(FuelStationOrTruckAction(c,out fuelHint,out fuelLabel,true))return;

            if(Near(c.Position,Station(ServiceKind.Meals)) && Shift.Meal==MealPhase.Idle){Shift.OrderMeal(c.Index);return;}
            foreach(var cart in Carts) if(cart.Kind!=ServiceKind.Fuel && cart.Owner==null && Near(c.Position,cart.Position,1.9f))
            {
                int cartId=Carts.IndexOf(cart);
                if(Shift.TryClaimCart(c.Index,cartId)){c.Cart=cart;cart.Owner=c;c.Position=cart.Position;return;}
            }
            if(Near(c.Position,Station(ServiceKind.Meals))) {Shift.OrderMeal(c.Index);return;}
            if(Near(c.Position,Station(ServiceKind.Boarding)))
            {
                string flightId=selected==null?null:selected.Id;
                if(Shift.IsBoarding(flightId))Shift.CloseGate(c.Index,flightId);else Shift.OpenGate(c.Index,flightId);
                return;
            }
        }
        void Release(Crew c)
        {
            if(c==null||c.Cart==null)return;
            int cartId=Carts.IndexOf(c.Cart);
            if(cartId>=0)Shift.ReleaseCart(c.Index,cartId);
            c.Cart.Owner=null;c.Cart.Slipping=false;c.Cart=null;
        }
        // ---------- 旅客：ShiftSim 参数化状态 + 表现层坐标还原 ----------
        static float[][] BuildPavementLegLengths()
        {
            var legs=new float[Level1Map.StandCount][];
            for(int s=0;s<Level1Map.StandCount;s++)
            {
                Vector3[] path=Level1Map.PavementPath(s);
                legs[s]=new float[Mathf.Max(0,path.Length-1)];
                for(int i=1;i<path.Length;i++)legs[s][i-1]=Vector3.Distance(path[i-1],path[i]);
            }
            return legs;
        }
        /// <summary>把旅客的 (stand, waypoint, progress01) 还原为世界坐标（阻车/渲染/回放共用）。</summary>
        public Vector3 PassengerPosition(ShiftPassenger p)
        {
            if(!p.Released)return PassengerQueuePosition(p.Stand,p.Seq);
            Vector3[] path=Level1Map.PavementPath(p.Stand);
            if(path==null || path.Length<2)return Level1Map.Station(ServiceKind.Boarding);
            int w=Mathf.Clamp(p.Waypoint,1,path.Length-1);
            Vector3 a=path[w-1],b=path[w];
            float travelled=Mathf.Clamp01(p.Progress01)*Shift.EffectiveLegLength(p.Stand,w);
            return Vector3.MoveTowards(a,b,travelled);
        }
        readonly Dictionary<string,Transform> passengerVisuals=new Dictionary<string,Transform>();
        readonly HashSet<string> passengerSeen=new HashSet<string>();
        /// <summary>Waiting passengers stay beside the gate; they never block the service road.</summary>
        static Vector3 PassengerQueuePosition(int stand,int seq)
        {
            return Station(ServiceKind.Boarding)+new Vector3(.25f+(seq%2)*.45f,0,.65f+(seq/2)*.48f+stand*.08f);
        }
        public bool PassengerBlocksSegment(Vector3 from,Vector3 to,ShiftPassenger passenger)
        {
            if(passenger==null || !passenger.Released)return false;
            Vector3 point=PassengerPosition(passenger);point.y=from.y;to.y=from.y;
            Vector3 segment=to-from;
            float t=segment.sqrMagnitude<.000001f?0f:Mathf.Clamp01(Vector3.Dot(point-from,segment)/segment.sqrMagnitude);
            return (point-(from+segment*t)).sqrMagnitude<.8f*.8f;
        }
        LineRenderer boardingRoute;
        Material boardingRouteMaterial;
        void SyncBoardingRoute(Flight flight)
        {
            if(flight==null || flight.Progress[(int)ServiceKind.Boarding]>=1f)
            {if(boardingRoute)boardingRoute.gameObject.SetActive(false);return;}
            if(!boardingRoute)
            {
                var route=new GameObject("Selected flight passenger route");route.transform.SetParent(transform,false);
                boardingRoute=route.AddComponent<LineRenderer>();
                boardingRouteMaterial=new Material(Shader.Find("Sprites/Default"));
                boardingRoute.sharedMaterial=boardingRouteMaterial;
                boardingRoute.startWidth=.09f;boardingRoute.endWidth=.09f;
                boardingRoute.startColor=AirportWorld.Hex("F9D478");boardingRoute.endColor=AirportWorld.Hex("F9D478");
                boardingRoute.useWorldSpace=true;
            }
            boardingRoute.gameObject.SetActive(true);
            Vector3[] path=Level1Map.PavementPath(flight.Stand);
            boardingRoute.positionCount=path.Length;
            for(int i=0;i<path.Length;i++)boardingRoute.SetPosition(i,path[i]+Vector3.up*.145f);
        }
        Transform EnsurePassengerVisual(string key,int seq,Vector3 position)
        {
            Transform visual;
            if(!passengerVisuals.TryGetValue(key,out visual)||!visual)
            {
                visual=AirportWorld.CreatePassenger("Passenger",AirportWorld.Hex(seq%2==0?"EAB5BE":"9EADCD"),position,seq);
                visual.localScale=Vector3.one*.65f;passengerVisuals[key]=visual;
            }
            return visual;
        }
        static string PaxKey(ShiftPassenger p){return p.FlightId+"#"+p.Seq;}
        void SyncPassengerVisuals()
        {
            if(Shift==null)return;
            passengerSeen.Clear();
            Flight selected= Crew.Count==0?null:SelectedFlight(Crew[Remote&&RemoteSeat>=0&&RemoteSeat<Crew.Count?RemoteSeat:0]);
            SyncBoardingRoute(selected);
            if(selected!=null && selected.Progress[(int)ServiceKind.Boarding]<1f)
            {
                bool manifest=false;
                for(int i=0;i<Shift.Passengers.Count;i++)if(Shift.Passengers[i].FlightId==selected.Id){manifest=true;break;}
                if(!manifest)
                {
                    int boarded=Mathf.RoundToInt(selected.Progress[(int)ServiceKind.Boarding]/ShiftSim.BoardingPerPassenger);
                    for(int seq=boarded;seq<ShiftSim.PassengersPerGate;seq++)
                    {
                        string key=selected.Id+"#"+seq;
                        passengerSeen.Add(key);
                        Transform visual=EnsurePassengerVisual(key,seq,PassengerQueuePosition(selected.Stand,seq));
                        visual.gameObject.SetActive(true);
                        visual.position=PassengerQueuePosition(selected.Stand,seq);
                    }
                }
            }
            for(int i=0;i<Shift.Passengers.Count;i++)
            {
                ShiftPassenger p=Shift.Passengers[i];
                string key=PaxKey(p); passengerSeen.Add(key);
                Transform visual;
                if(!passengerVisuals.TryGetValue(key,out visual) || visual==null)
                {
                    visual=AirportWorld.CreatePassenger("Passenger",AirportWorld.Hex(p.Seq%2==0?"EAB5BE":"9EADCD"),PassengerPosition(p),p.Seq);
                    visual.localScale=Vector3.one*.65f;
                    passengerVisuals[key]=visual;
                }
                visual.gameObject.SetActive(true);
                Vector3 pos=PassengerPosition(p);
                Vector3 delta=pos-visual.position;
                if(delta.sqrMagnitude>.001f)visual.rotation=Quaternion.LookRotation(delta);
                visual.position=pos;
            }
            if(passengerVisuals.Count==passengerSeen.Count)return;
            var stale=new List<string>();
            foreach(var kv in passengerVisuals)if(!passengerSeen.Contains(kv.Key))stale.Add(kv.Key);
            for(int i=0;i<stale.Count;i++){var visual=passengerVisuals[stale[i]];if(visual)Destroy(visual.gameObject);passengerVisuals.Remove(stale[i]);}
        }
        void ClearPassengerVisuals()
        {
            foreach(var kv in passengerVisuals)if(kv.Value)Destroy(kv.Value.gameObject);
            passengerVisuals.Clear();
            if(boardingRoute)Destroy(boardingRoute.gameObject);boardingRoute=null;
            if(boardingRouteMaterial)Destroy(boardingRouteMaterial);boardingRouteMaterial=null;
        }
        CrewInput BotInput(Crew c,float dt)
        {
            Flight f=SelectedFlight(c);
            int preferred=-1;
            bool continuingFuel=c.Cart!=null && c.Cart.Kind==ServiceKind.Fuel || Shift.NozzleState==NozzlePhase.Held&&Shift.NozzleSeat==c.Index || Shift.ValveOpen || Shift.NozzleState==NozzlePhase.OnTruck || Shift.HoseState==HosePhase.OnAircraft || Shift.HoseState==HosePhase.Held&&Shift.HoseSeat==c.Index;
            if(f!=null && c.Cart==null && !continuingFuel)
            {
                bool[] available=new bool[4];
                for(int k=0;k<3;k++)available[k]=f.Progress[k]<1f && !Shift.IsTaskFailed(f.Id,(ServiceKind)k) && Carts[k].Owner==null;
                if(Shift.NozzleState==NozzlePhase.Held && Shift.NozzleSeat!=c.Index || Shift.HoseState==HosePhase.Held && Shift.HoseSeat!=c.Index)available[(int)ServiceKind.Fuel]=false;
                available[(int)ServiceKind.Boarding]=!Shift.IsTaskFailed(f.Id,ServiceKind.Boarding) && f.Progress[0]>=1f && f.Progress[2]>=1f && f.Progress[3]<1f && !Shift.IsBoarding(f.Id) && Shift.StandReady(f.Stand);
                preferred=AppState.Ensure().BotMemory.ChooseBotTask(available);
            }
            CrewInput fuelInput;
            if((continuingFuel || preferred<0 || preferred==(int)ServiceKind.Fuel) && TryBotFuelInput(c,dt,f,out fuelInput))return fuelInput;
            return BotGeneralInput(c,dt,f,preferred);
        }

        bool TryBotFuelInput(Crew c,float dt,Flight targetFlight,out CrewInput result)
        {
            result=new CrewInput();
            if(c.Cart!=null && c.Cart.Kind!=ServiceKind.Fuel)return false;

            // M3.3r bot 路径：拿枪—插车—开阀—等待—关阀（自动归位）—驾驶—取管—接管—等待—断管（自动收回）—驾驶。
            bool taskNeedsFuel=targetFlight!=null && FlightNeedsFuel(targetFlight);
            bool ownsNozzle=Shift.NozzleState==NozzlePhase.Held && Shift.NozzleSeat==c.Index;
            bool ownsHose=Shift.HoseState==HosePhase.Held && Shift.HoseSeat==c.Index;
            bool hoseOnAircraft=Shift.HoseState==HosePhase.OnAircraft;
            bool hasFuelWork=taskNeedsFuel || Shift.ValveOpen || Shift.NozzleState==NozzlePhase.OnTruck ||
                ownsNozzle || ownsHose || hoseOnAircraft;
            if(!hasFuelWork)
            {
                // Do not leave a bot sitting in a fuel truck after its flight is done.
                if(c.Cart!=null && c.Cart.Kind==ServiceKind.Fuel)
                {
                    result=BotMoveInput(c,dt,c.Position,true,false);
                    return true;
                }
                return false;
            }

            // A player who already holds the station nozzle or the truck hose keeps
            // exclusive control; the bot releases a parked fuel truck and does other work.
            if((Shift.NozzleState==NozzlePhase.Held && !ownsNozzle && !Shift.ValveOpen) ||
               (Shift.HoseState==HosePhase.Held && !ownsHose))
            {
                if(c.Cart!=null && c.Cart.Kind==ServiceKind.Fuel)
                {
                    result=BotMoveInput(c,dt,c.Position,true,false);
                    return true;
                }
                return false;
            }

            float needed=taskNeedsFuel?1f-targetFlight.Progress[(int)ServiceKind.Fuel]:0f;
            bool tankCoversTask=Shift.FuelTruckTank>=Mathf.Min(1f,needed)-.0001f;
            Vector3 target=c.Position; bool act=false;
            if(c.Cart!=null && c.Cart.Kind==ServiceKind.Fuel)
            {
                if(!Shift.CanDriveCart((int)ServiceKind.Fuel))
                {
                    // Valve, inserted nozzle or hose must be handled on foot.
                    target=c.Position;act=true;
                }
                else if(taskNeedsFuel && Shift.FuelTruckTank>0f && (tankCoversTask || !Shift.FuelTruckAtStation))
                    target=Dock(targetFlight.Stand);
                else
                    target=Level1Map.CartPark(ServiceKind.Fuel); // 站边加注位：在油站加注区内、但不压住阀门交互圈
                if(Vector3.Distance(c.Position,target)<1.1f)act=true;
                result=BotMoveInput(c,dt,target,act,false);
                return true;
            }

            if(Shift.ValveOpen)
            {
                // Keep filling without repeated interaction; close (auto-returning the
                // nozzle) when full, or immediately when the valve is not filling.
                target=Station(ServiceKind.Fuel);
                bool filling=Shift.NozzleState==NozzlePhase.OnTruck && Shift.FuelTruckAtStation;
                act=Vector3.Distance(c.Position,target)<1.1f &&
                    (!filling || Shift.FuelTruckTank>=1f || !taskNeedsFuel || tankCoversTask);
            }
            else if(hoseOnAircraft)
            {
                Flight connected=Shift.FindFlight(Shift.HoseFlightId);
                if(connected==null || connected.Stand<0){result=new CrewInput();return true;}
                target=Dock(connected.Stand);
                bool keepFuelling=FlightNeedsFuel(connected) && Shift.FuelTruckTank>0f;
                act=!keepFuelling && Vector3.Distance(c.Position,target)<1.1f;
            }
            else if(ownsHose)
            {
                int stand=FuelTruckDockStand();
                Flight dockFlight=stand>=0?Sim.ActiveAtStand(stand):null;
                if(dockFlight!=null && FlightNeedsFuel(dockFlight) && Shift.FuelTruckTank>0f)target=Dock(stand);
                else target=FuelCart.Position;
                act=Vector3.Distance(c.Position,target)<1.1f;
            }
            else if(ownsNozzle)
            {
                if(taskNeedsFuel && Shift.FuelTruckAtStation && Shift.FuelTruckTank<1f && !tankCoversTask)
                    target=FuelCart.Position;   // tap truck → insert
                else
                    target=Station(ServiceKind.Fuel);   // tap station → return
                act=Vector3.Distance(c.Position,target)<1.1f;
            }
            else if(Shift.NozzleState==NozzlePhase.OnTruck)
            {
                // Inserted, valve closed: open it to fill, or pull the nozzle back when not needed.
                if(taskNeedsFuel && Shift.FuelTruckTank<1f && !tankCoversTask)target=Station(ServiceKind.Fuel);
                else target=FuelCart.Position;
                act=Vector3.Distance(c.Position,target)<1.1f;
            }
            else
            {
                if(!taskNeedsFuel)return false;
                if(FuelCart.Owner!=null)return false;
                if(FuelTruckAtDock(targetFlight.Stand) && Shift.FuelTruckTank>0f)
                    target=FuelCart.Position;   // tap truck → take hose (priority rule)
                else if(Shift.FuelTruckAtStation && !tankCoversTask && Shift.NozzleState==NozzlePhase.AtStation)
                    target=Station(ServiceKind.Fuel);   // take nozzle
                else
                    target=FuelCart.Position;   // tap truck → drive
                act=Vector3.Distance(c.Position,target)<1.1f;
            }

            result=BotMoveInput(c,dt,target,act,false);
            return true;
        }

        CrewInput BotGeneralInput(Crew c,float dt,Flight f,int preferred=-1)
        {
            // M3.1 起任务练习（Sandbox）bot 与本地班岗 bot 走同一任务主循环（不绕过竞争，M0 约定）。
            if(f==null)return new CrewInput();
            Vector3 target=c.Position; bool act=false;
            if(c.Cart!=null)
            {
                var cart=c.Cart; var state=CartState(cart);
                Flight cargo=state.Loaded?Shift.FindFlight(state.CargoFlightId):null;
                bool orphaned=state.Loaded && (cargo==null || cargo.Status!=FlightStatus.Servicing);
                if(state.Loaded && !orphaned) target=state.Arrival?Station(cart.Kind):Dock(cargo.Stand);
                else if(orphaned) target=Station(cart.Kind);
                else if(f.Progress[(int)cart.Kind]>=1) {Release(c);return new CrewInput();}
                else target=cart.Kind==ServiceKind.Baggage&&!f.ArrivalBagsReturned?Dock(f.Stand):Station(cart.Kind);
            }
            else
            {
                if((preferred<0 || preferred==(int)ServiceKind.Boarding) && f.Progress[0]>=1 && f.Progress[2]>=1 && f.Progress[(int)ServiceKind.Boarding]<1f && !Shift.IsBoarding(f.Id)) target=Station(ServiceKind.Boarding);
                else
                {
                    Cart chosen=null;foreach(var cart in Carts)if(cart.Kind!=ServiceKind.Fuel&&(preferred<0 || preferred==(int)cart.Kind)&&cart.Owner==null&&f.Progress[(int)cart.Kind]<1){chosen=cart;break;}
                    if(chosen==null)return new CrewInput();target=chosen.Position;
                }
            }
            if(Vector3.Distance(c.Position,target)<1.1f)act=true;
            return BotMoveInput(c,dt,target,act,act);
        }

        CrewInput BotMoveInput(Crew c,float dt,Vector3 target,bool act,bool held)
        {
            Vector3 delta=target-c.Position;
            if(c.Cart!=null && !act)
            {
                // Plan only when the job changes or the bot has been blocked;
                // follow the cached turns precisely instead of rebuilding a grid every frame.
                if(Vector3.Distance(c.Position,c.BotLastPosition)>.01f)c.BotLastMovedAt=Sim.Elapsed;
                c.BotLastPosition=c.Position;
                bool stalled=Sim.Elapsed-c.BotLastMovedAt>1.5f;
                if(c.BotRoute==null || c.BotRouteCart!=c.Cart || Vector3.Distance(c.BotRouteTarget,target)>.05f || stalled)
                {
                    var obstacles=new List<Vector3>();foreach(var other in Carts)if(other!=c.Cart)obstacles.Add(other.Position);
                    var planned=Level1Map.DriveRoute(c.Position,target,obstacles);
                    c.BotRouteTarget=target;c.BotRouteCart=c.Cart;c.BotLastMovedAt=Sim.Elapsed;
                    if(planned==null)
                    {
                        // 无路可达（如道路被乱停的车辆堵死）：原地等待、逐帧重试，
                        // 绝不能退回直线硬闯——非道路区会把车卡死。
                        c.BotRoute=null;
                        return new CrewInput();
                    }
                    var route=new List<Vector3>(planned);route.Add(target);
                    c.BotRoute=route.ToArray();c.BotWaypoint=0;
                }
                while(c.BotWaypoint<c.BotRoute.Length-1 && Vector3.Distance(c.Position,c.BotRoute[c.BotWaypoint])<.02f)c.BotWaypoint++;
                delta=c.BotRoute[c.BotWaypoint]-c.Position;
            }
            else if(c.Cart==null){c.BotRoute=null;c.BotRouteCart=null;}
            bool press=act && Sim.Elapsed>=c.NextActionAt;
            if(press)c.NextActionAt=Sim.Elapsed+.35f;
            // 接近目标按本帧实际步长限速：帧率骤降时步长会远超 0.18m 减速圈，
            // 固定 0.18 会让 bot 在路点两侧来回过冲、永远进不了到达圈（表现为原地卡死）。
            float slowRadius=Mathf.Max(.18f,(c.Cart==null?4.5f:3.6f)*dt);
            return new CrewInput{Move=act?Vector2.zero:new Vector2(delta.x,delta.z).normalized*Mathf.Min(1,delta.magnitude/slowRadius), Held=held||press, Pressed=press};
        }
        void UpdateVisuals()
        {
            if(Sim==null)return;
            for(int s=0;s<Level1Map.StandCount;s++)
            {
                Flight f=Sim.ActiveAtStand(s);
                // 以航班号判同而非引用：client 镜像每快照重建 Flight 对象，
                // 引用比对会让飞机每帧销毁重建；host 端行为与旧引用比对一致。
                bool same=shown[s]==null ? f==null : (f!=null && shown[s].Id==f.Id);
                if(same)continue;
                if(planes[s])
                {
                    Flight previous=shown[s]==null || Shift==null ? null : Shift.FindFlight(shown[s].Id);
                    if(previous!=null && previous.Status==FlightStatus.Departed)BeginTakeOff(planes[s],s);
                    else Destroy(planes[s].gameObject);
                }
                shown[s]=f;
                if(f!=null)
                {
                    // Landing aircraft taxi in from the runway before the
                    // stand opens for ground work.
                    planes[s]=AirportWorld.CreatePlane(f.Id,Level1Map.TaxiInPath(s)[0]);
                    if((Remote || restoringPlanes) && Shift.StandReady(s))
                    {
                        planes[s].position=Level1Map.PlanePark(s);
                        planes[s].rotation=AircraftMotion.NoseRotation(AircraftMotion.FinalDirection(Level1Map.TaxiInPath(s)));
                    }
                    else
                    {
                        BeginTaxiIn(planes[s],s);
                        AdvanceMotion(motions[motions.Count-1],Mathf.Max(0,DisplayElapsed-f.AssignedAt));
                    }
                }
            }
            foreach(var c in Crew) { c.Visual.position=c.Position+Vector3.up*(c.Cart!=null?.35f:0); }
            for(int i=0;i<Carts.Count;i++)
            {
                var c=Carts[i];
                c.Visual.position=c.Position; c.Cargo.gameObject.SetActive(i<Shift.Carts.Length && Shift.Carts[i].Loaded);
            }
            SyncFuelEquipmentVisuals();
        }
        // ---------- 飞机滑行：Step 驱动的分段动画 ----------
        // 原协程实现在 batchmode 下随播放器循环停摆，导致截图工具无法确定性推进；
        // 改为由 Step 调用 UpdatePlaneMotions 逐段推进，实时与固定步长行为一致。
        sealed class PlaneMotion
        {
            public Transform Plane; public int Stand;
            public readonly List<MotionLeg> Legs = new List<MotionLeg>();
            public int Leg; public float Travelled; public float Elapsed;
        }
        sealed class MotionLeg
        {
            public int Kind; // 0 滑行段(Reverse=倒退) 1 停顿 2 拉起爬升 3 停稳入位
            public Vector3 From, To; public float Speed; public float Duration; public bool Reverse;
        }
        static void AddTaxiLegs(PlaneMotion m, Vector3[] path, int startIndex, float speed, int endIndex, bool reverse)
        {
            if(path==null || path.Length<2) return;
            int first=Mathf.Clamp(startIndex,1,path.Length-1);
            int last=endIndex<0?path.Length-1:Mathf.Clamp(endIndex,first,path.Length-1);
            for(int i=first;i<=last;i++)
                m.Legs.Add(new MotionLeg{Kind=0,From=path[i-1],To=path[i],Speed=speed,Reverse=reverse});
        }
        void UpdatePlaneMotions(float dt)
        {
            for(int i=motions.Count-1;i>=0;i--)
            {
                PlaneMotion m=motions[i];
                if(m.Plane==null || Sim==null || Sim.Finished)
                {
                    if(m.Plane) Destroy(m.Plane.gameObject);
                    motions.RemoveAt(i); continue;
                }
                if(Paused || Help) continue;
                AdvanceMotion(m,dt);
                if(m.Leg>=m.Legs.Count) motions.RemoveAt(i);
            }
        }
        void AdvanceMotion(PlaneMotion m,float dt)
        {
            while(m.Leg<m.Legs.Count)
            {
                MotionLeg leg=m.Legs[m.Leg];
                float remaining=leg.Kind==0 ? Mathf.Max(0,(Vector3.Distance(leg.From,leg.To)-m.Travelled)/leg.Speed) : leg.Kind==3 ? 0 : Mathf.Max(0,leg.Duration-m.Elapsed);
                float step=Mathf.Min(dt,remaining);
                if(!AdvanceLeg(m,leg,step))break;
                dt=Mathf.Max(0,dt-step);m.Leg++;m.Travelled=0;m.Elapsed=0;
                if(dt<=0 && (m.Leg>=m.Legs.Count || m.Legs[m.Leg].Kind!=3))break;
            }
        }
        bool AdvanceLeg(PlaneMotion m, MotionLeg leg, float dt)
        {
            Transform plane=m.Plane;
            switch(leg.Kind)
            {
                case 0: // 滑行段：沿段前进，机头对齐（倒退时机头反向）
                {
                    Vector3 dir=leg.To-leg.From;
                    float dist=dir.magnitude;
                    if(dist>.001f) plane.rotation=AircraftMotion.NoseRotation(leg.Reverse?-dir:dir);
                    m.Travelled=Mathf.Min(m.Travelled+leg.Speed*dt,dist);
                    plane.position=Vector3.MoveTowards(leg.From,leg.To,m.Travelled);
                    if(leg.Reverse && dist>.001f) plane.rotation=AircraftMotion.NoseRotation(-dir);
                    return m.Travelled>=dist-.0001f;
                }
                case 1: m.Elapsed+=dt; return m.Elapsed>=leg.Duration; // 停顿
                case 2: // 拉起爬升：沿最终滑行道切线加速离舰
                {
                    if(m.Elapsed==0){leg.From=plane.position;plane.rotation=AircraftMotion.NoseRotation(leg.To);}
                    m.Elapsed+=dt;
                    float e=Mathf.Min(m.Elapsed,leg.Duration);
                    plane.position=leg.From+leg.To*(e*e*6f)+Vector3.up*(e*e*2.5f);
                    if(m.Elapsed<leg.Duration)return false;
                    if(plane)Destroy(plane.gameObject);
                    return true;
                }
                default: // 3 停稳入位：机头保持最终进场切线，开放站坪作业
                    plane.position=Level1Map.PlanePark(m.Stand);
                    plane.rotation=AircraftMotion.NoseRotation(AircraftMotion.FinalDirection(Level1Map.TaxiInPath(m.Stand)));
                    if(!Remote)Shift.SetStandReady(m.Stand,true);
                    return true;
            }
        }
        void BeginTaxiIn(Transform plane, int stand)
        {
            if(!Remote)Shift.SetStandReady(stand,false);
            Vector3[] path=Level1Map.TaxiInPath(stand);
            var m=new PlaneMotion{Plane=plane,Stand=stand};
            // Fast roll-out on the runway, slower taxi on the stub.
            int runwayEnd=Mathf.Min(2,path.Length-1);
            AddTaxiLegs(m,path,1,6f,runwayEnd,false);
            AddTaxiLegs(m,path,runwayEnd,3.2f,-1,false);
            m.Legs.Add(new MotionLeg{Kind=3});
            motions.Add(m);
        }
        void BeginTakeOff(Transform plane, int stand)
        {
            Shift.SetStandReady(stand,false);
            Vector3[] arrival=Level1Map.TaxiInPath(stand);
            Vector3[] path=Level1Map.TaxiOutPath(stand);
            int forwardStart=Mathf.Clamp(AircraftMotion.PushbackEndIndex(path),0,path.Length-1);
            Vector3[] pushback=AircraftMotion.BuildPushbackPath(arrival,path);
            Vector3[] forwardPath=AircraftMotion.BuildForwardTaxiPath(path,forwardStart,pushback);
            var m=new PlaneMotion{Plane=plane,Stand=stand};

            // Push out of the stand while the nose faces backwards relative
            // to travel. This prefix deliberately reaches the first taxiway
            // bend instead of turning at the first point still inside ramp.
            AddTaxiLegs(m,pushback,1,4.2f,-1,true);
            // The final reverse-curve tangent already points the nose along
            // the next forward tangent. Let that alignment read for a short
            // beat before the aircraft begins taxiing.
            m.Legs.Add(new MotionLeg{Kind=1,Duration=.18f});
            // Taxi forward from the same authored bend; every subsequent
            // corner updates the nose from the path tangent.
            Vector3 forward=AircraftMotion.FirstDirection(forwardPath,0,
                AircraftMotion.FinalDirection(arrival));
            AddTaxiLegs(m,forwardPath,1,4.2f,-1,false);
            // Accelerate along the final taxiway tangent and climb away.
            m.Legs.Add(new MotionLeg{Kind=2,Duration=2.2f,To=AircraftMotion.FinalDirection(forwardPath,forward)});
            motions.Add(m);
        }
        void Finish()
        {
            if(Sandbox || Sim==null || wasFinished) return; wasFinished=true;
            if(!NetworkShift)Record();
            replayLastEventSeq=0;
            CompleteSettlement();
            ClearInput();
            AppState.Ensure().RecordCoopMatch(NetworkShift ? AppState.Ensure().Room.MatchId+":"+RoundId : localMatchId,Sim.DepartedCount,Shift.HumanTaskCounts);
            foreach(var m in motions)if(m.Plane)Destroy(m.Plane.gameObject);motions.Clear();
            foreach(var kv in passengerVisuals)if(kv.Value)kv.Value.gameObject.SetActive(false);
            Notify("班岗结束 · 一起看看刚才的忙碌时刻");
            Level1Map.ConfigureCamera(View,true);
        }
        public void BackToLobby()
        {
            CancelReplayExport();
            AppState state=AppState.Ensure();
            if(state.Room!=null)state.Room.Leave();
            if(state.Beacon!=null)state.Beacon.Stop();
            state.Launch.Reset();
            Paused=false;Help=false;Started=false;
            SceneManager.LoadScene(AppState.SceneCabinLobby);
        }
        public void Retry()
        {
            if(!CanRetry && MatchFinished)return;
            CancelReplayExport();
            if(NetworkShift){if(CanRetry && AppState.Ensure().Room!=null)AppState.Ensure().Room.RetryShift();return;}
            if(Remote){StartRemote();return;}
            if(Sandbox){StartSandbox();return;}
            StartShift(Solo);
        }
        sealed class ReplayFrame
        {
            public float Time; public Vector3[] Positions; public Quaternion[] Rotations;
            public bool[] Loaded; public Flight[] Flights; public Vector3[] People;
            public ShiftSnap FuelShift;
            public string[] FailedTasks = new string[0];
            public ShiftEventSnap[] Events = new ShiftEventSnap[0];
        }
        /// <summary>从权威采样构造回放帧；host/client 使用相同纯数据，按索引补齐乱序与丢包。</summary>
        public void RecordNetworkSnapshot(Snapshot snapshot)
        {
            if(snapshot==null || snapshot.ReplayIndex<0 || networkReplayFrames.ContainsKey(snapshot.ReplayIndex))return;
            var f=new ReplayFrame {Time=snapshot.Elapsed,Positions=new Vector3[5],Rotations=new Quaternion[5],Loaded=new bool[3],Flights=new Flight[Level1Map.StandCount]};
            for(int i=0;i<2;i++)
            {
                CrewSnap c=snapshot.Crew[i];f.Positions[i]=new Vector3(c.X,c.HeldCart>=0?.35f:0,c.Z);f.Rotations[i]=Quaternion.Euler(0,c.RotY,0);
            }
            for(int i=0;i<3;i++){CartSnap c=snapshot.Carts[i];f.Positions[i+2]=new Vector3(c.X,0,c.Z);f.Rotations[i+2]=Quaternion.Euler(0,c.RotY,0);f.Loaded[i]=c.Cargo!=0;}
            if(snapshot.Flights!=null)foreach(var source in snapshot.Flights)
            {
                if(source==null || source.Status!=(int)FlightStatus.Servicing || source.Stand<0 || source.Stand>=f.Flights.Length)continue;
                f.Flights[source.Stand]=new Flight(source.Id,source.Arrival,source.Deadline,source.Stand) {Status=FlightStatus.Servicing,Progress=(float[])source.Progress.Clone(),ArrivalBagsReturned=source.ArrivalBagsReturned};
            }
            f.FuelShift=snapshot.Shift??new ShiftSnap();f.FailedTasks=f.FuelShift.Failed??new string[0];
            f.Events=f.FuelShift.Events??new ShiftEventSnap[0];
            PassengerSnap[] people=f.FuelShift.Passengers??new PassengerSnap[0];f.People=new Vector3[people.Length];
            for(int i=0;i<people.Length;i++)
            {
                PassengerSnap person=people[i];f.People[i]=PassengerPosition(new ShiftPassenger {FlightId=person.FlightId,Seq=person.Seq,Stand=person.Stand,Waypoint=person.Waypoint,Progress01=person.Progress01,Delay=person.Delay,Released=person.Released});
            }
            networkReplayFrames[snapshot.ReplayIndex]=f;
            replay.Clear();foreach(var pair in networkReplayFrames)replay.Add(pair.Value);
        }

        void Record()
        {
            if(Sim==null || Sandbox)return;
            var f=new ReplayFrame {Time=Sim.Elapsed,Positions=new Vector3[5],Rotations=new Quaternion[5],Loaded=new bool[3],Flights=new Flight[Level1Map.StandCount],People=new Vector3[Shift.Passengers.Count]};
            for(int i=0;i<2;i++){f.Positions[i]=Crew[i].Visual.position;f.Rotations[i]=Crew[i].Visual.rotation;}
            for(int i=0;i<Level1Map.StandCount;i++)
            {
                Flight live=Sim.ActiveAtStand(i);
                if(live!=null)f.Flights[i]=new Flight(live.Id,live.Destination,live.ArrivalTime,live.Deadline,live.Stand)
                {Status=live.Status,ArrivalBagsReturned=live.ArrivalBagsReturned,Progress=(float[])live.Progress.Clone()};
                if(live!=null)for(int k=0;k<4;k++)f.Flights[i].SetTaskFailed((ServiceKind)k,live.IsTaskFailed((ServiceKind)k));
            }
            for(int i=0;i<3;i++){f.Positions[i+2]=Carts[i].Position;f.Rotations[i+2]=Carts[i].Visual.rotation;f.Loaded[i]=i<Shift.Carts.Length && Shift.Carts[i].Loaded;}
            for(int i=0;i<Shift.Passengers.Count;i++)f.People[i]=PassengerPosition(Shift.Passengers[i]);
            ShiftSnap shiftSnapshot=Shift.CaptureShift();
            f.FuelShift=shiftSnapshot;
            f.FailedTasks=shiftSnapshot.Failed==null?new string[0]:(string[])shiftSnapshot.Failed.Clone();
            var newEvents=new List<ShiftEventSnap>();
            if(shiftSnapshot.Events!=null)
            {
                for(int i=0;i<shiftSnapshot.Events.Length;i++)
                {
                    ShiftEventSnap e=shiftSnapshot.Events[i];
                    if(e==null || e.Sequence<=replayLastEventSeq)continue;
                    newEvents.Add(e);
                    if(e.Sequence>replayLastEventSeq)replayLastEventSeq=e.Sequence;
                }
            }
            f.Events=newEvents.ToArray();
            replay.Add(f);
        }
        readonly List<Transform> replayPeople=new List<Transform>();
        void PlayReplay(float dt)
        {
            if(Sim==null || Sandbox || replay.Count==0 || NetworkShift&&!networkReplayReady)return;
            if(!NetworkShift)replayTime=Mathf.Min(replayTime+dt*12,replay[replay.Count-1].Time);
            int previousIndex=replayIndex;
            while(replayIndex<replay.Count-1 && replay[replayIndex+1].Time<=replayTime)replayIndex++;
            var f=replay[replayIndex];
            // Play events only when crossing into their recorded frame. Re-reading
            // the current frame on a slow/stalled replay would repeat the toast.
            for(int frameIndex=previousIndex+1;frameIndex<=replayIndex;frameIndex++)
            {
                ReplayFrame frame=replay[frameIndex];
                if(frame.Events==null)continue;
                for(int eventIndex=0;eventIndex<frame.Events.Length;eventIndex++)
                {
                    ShiftEventSnap e=frame.Events[eventIndex];
                    if(e==null || e.Sequence<=replayLastEventSeq)continue;
                    replayLastEventSeq=e.Sequence;
                    if(e.Type==ShiftEventTypes.TaskFailed || e.Type==ShiftEventTypes.SpillStarted ||
                       e.Type==ShiftEventTypes.SpillCleared || e.Type==ShiftEventTypes.ValveOpened ||
                       e.Type==ShiftEventTypes.ValveClosed || e.Type==ShiftEventTypes.NozzleTaken ||
                       e.Type==ShiftEventTypes.NozzleAttached || e.Type==ShiftEventTypes.NozzleReturned ||
                       e.Type==ShiftEventTypes.HoseTaken || e.Type==ShiftEventTypes.HoseAttached ||
                       e.Type==ShiftEventTypes.HoseDetached || e.Type==ShiftEventTypes.HoseReturned ||
                       (e.Type==ShiftEventTypes.Delivered && e.Kind==(int)ServiceKind.Fuel))
                        ShowReplayNotice(e.Text);
                }
            }
            ApplyReplayFrame(f);
        }

        void ApplyReplayFrame(ReplayFrame f)
        {
            for(int i=0;i<2;i++){Crew[i].Visual.position=f.Positions[i];Crew[i].Visual.rotation=f.Rotations[i];}
            for(int i=0;i<Level1Map.StandCount;i++)
            {
                bool same=shown[i]==null?f.Flights[i]==null:(f.Flights[i]!=null&&shown[i].Id==f.Flights[i].Id);
                if(!same)
                {
                    if(planes[i]) { planes[i].gameObject.SetActive(false); Destroy(planes[i].gameObject); } shown[i]=f.Flights[i];
                    if(shown[i]!=null)
                    {
                        planes[i]=AirportWorld.CreatePlane(shown[i].Id,Level1Map.PlanePark(i));
                        planes[i].rotation=AircraftMotion.NoseRotation(AircraftMotion.FinalDirection(Level1Map.TaxiInPath(i)));
                    }
                }
            }
            for(int i=0;i<3;i++){Carts[i].Visual.position=f.Positions[i+2];Carts[i].Visual.rotation=f.Rotations[i+2];Carts[i].Cargo.gameObject.SetActive(f.Loaded[i]);}
            SyncFuelEquipmentVisuals(f.FuelShift,f.Flights);
            while(replayPeople.Count<f.People.Length){var person=AirportWorld.CreatePassenger("Replay passenger",AirportWorld.Hex(replayPeople.Count%2==0?"EAB5BE":"9EADCD"),Vector3.zero,replayPeople.Count);person.localScale=Vector3.one*.65f;replayPeople.Add(person);}
            for(int i=0;i<replayPeople.Count;i++){replayPeople[i].gameObject.SetActive(i<f.People.Length);if(i<f.People.Length)replayPeople[i].position=f.People[i];}
        }
    }
}
