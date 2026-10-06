using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IslandAirport
{
    public sealed class NetSession
    {
        static string lobbyToast = string.Empty;

        readonly AirportGame game;
        readonly RoomManager room;
        float clock;
        float snapshotElapsed;
        float inputElapsed;
        int inputSeq;
        int snapshotTick;
        int toastSeq;
        string lastToast = string.Empty;
        Vector2 pendingMove;
        bool pendingPressed;
        bool pendingHeld;
        bool pendingCycle;
        bool disposed;
        readonly List<Snapshot> history = new List<Snapshot>();
        Snapshot finalWorld;
        readonly HashSet<int> permanentlyMissing = new HashSet<int>();
        float nextRecordAt, repairAt;
        int expectedReplayCount;
        bool replayStarted, replayAckSent;
        const int ReplayLimit = 602;
        const float ReplayPeriod = .5f;
        public int ReplayCount { get { int n=0;foreach(var frame in history)if(frame!=null)n++;return n; } }
        public string ReplayHash(int index)
        {
            if(index<0 || index>=history.Count || history[index]==null)return string.Empty;
            string encoded=NetProtocol.BuildSnapshot(history[index]);
            uint hash=2166136261;
            foreach(char c in encoded)hash=(hash^c)*16777619;
            return hash.ToString("X8");
        }
        public void ResetRound()
        {
            ResetPendingInput();finalWorld=null;history.Clear();permanentlyMissing.Clear();
            nextRecordAt=repairAt=0;expectedReplayCount=0;replayStarted=replayAckSent=false;
            snapshotElapsed=inputElapsed=0;snapshotTick=0;
        }

        public NetSession(AirportGame airportGame, RoomManager roomManager)
        {
            if (airportGame == null) throw new ArgumentNullException("airportGame");
            if (roomManager == null) throw new ArgumentNullException("roomManager");
            game = airportGame;
            room = roomManager;
        }

        public float Clock { get { return clock; } }
        public int InputSequence { get { return inputSeq; } }
        public int SnapshotSequence { get { return snapshotTick; } }
        public bool ControlsRemoteSeat(int seat) { return !disposed && room.IsRemoteSeat(seat); }
        public bool SeatIsBot(int seat) { return seat >= 0 && seat < room.Seats.Length && room.Seats[seat].Bot; }

        public static string TakeLobbyToast()
        {
            string message = lobbyToast;
            lobbyToast = string.Empty;
            return message;
        }

        public bool Pump(float dt)
        {
            if (disposed || !ValidDelta(dt)) return !disposed;
            if (AppState.Ensure().NetworkAuthInvalidated)
            {
                ReturnToLobby("登录已失效，已离开联机班岗。请重新登录后组队。");
                return false;
            }
            clock += dt;
            UpdateBeacon();
            string evt;
            while (room.TryDequeue(out evt))
            {
                if (evt == "migrated")
                {
                    AcceptHistory(room.MigrationSnapshot);
                    expectedReplayCount=Math.Max(expectedReplayCount,Math.Min(ReplayLimit,room.MigrationSnapshot.ReplayCount));
                    game.PromoteAuthority(room.MigrationSnapshot);
                    permanentlyMissing.Clear();
                    for(int i=0;i<expectedReplayCount;i++)if(i>=history.Count || history[i]==null)permanentlyMissing.Add(i);
                    while(history.Count<expectedReplayCount)history.Add(null);
                    nextRecordAt=game.DisplayElapsed+ReplayPeriod;
                    replayStarted=false;replayAckSent=false;
                    game.SetNetworkReplayClock(0,false);
                    ResetPendingInput();RefreshCrewNames();UpdateReplayStatus();
                    continue;
                }
                if(evt == "retry") {game.OnNetworkRetry();continue;}
                if(evt == "replayready") {replayStarted=true;game.SetNetworkReplayClock(0,true);continue;}
                if(evt.StartsWith("replayrequest:",StringComparison.Ordinal))
                {
                    int index;
                    if(int.TryParse(evt.Substring(14),out index) && index>=0 && index<history.Count && history[index]!=null)room.SendReplayFrame(index,history[index]);
                    continue;
                }
                if (evt == "hostlost")
                {
                    ReturnToLobby("房主已离开，已返回大厅。");
                    return false;
                }
                if (evt == "closed")
                {
                    ReturnToLobby("房间已关闭，已返回大厅。");
                    return false;
                }
                if (evt.StartsWith("left:", StringComparison.Ordinal))
                {
                    int seat;
                    if (int.TryParse(evt.Substring(5), out seat) && seat >= 0 && seat < game.Crew.Count)
                    {
                        game.Crew[seat].Bot = true;
                        game.Notify("搭档已离开 · BOT 接管岗位。");
                    }
                }
                if (evt == "roster" || evt.StartsWith("joined:", StringComparison.Ordinal))
                    RefreshCrewNames();
            }
            if(game.NetworkShift && !room.IsHost)
            {
                Snapshot frame;
                while(room.TryTakeReplaySnapshot(out frame))AcceptHistory(frame);
                if(clock>=repairAt)
                {
                    repairAt=clock+.5f;
                    int requested=0;
                    for(int i=0;i<expectedReplayCount && requested<4;i++)
                        if(!permanentlyMissing.Contains(i) && (i>=history.Count || history[i]==null)) {room.RequestReplayFrame(i);requested++;}
                }
                if(game.MatchFinished && HistoryComplete() && !replayAckSent) {room.ReplayReady();replayAckSent=true;}
            }
            return true;
        }

        bool HistoryComplete()
        {
            if(expectedReplayCount<=0)return false;
            for(int i=0;i<expectedReplayCount;i++)if(!permanentlyMissing.Contains(i) && (i>=history.Count || history[i]==null))return false;
            return true;
        }
        void AcceptHistory(Snapshot snapshot)
        {
            if(snapshot==null || !game.NetworkShift)return;
            if(snapshot.ReplayIndex>=0 && snapshot.ReplayIndex<ReplayLimit)
            {
                while(history.Count<=snapshot.ReplayIndex)history.Add(null);
                if(history[snapshot.ReplayIndex]==null)
                {history[snapshot.ReplayIndex]=snapshot;game.RecordNetworkSnapshot(snapshot);}
            }
            if(snapshot.RoundId==room.RoundId && snapshot.AuthorityEpoch==room.AuthorityEpoch)
            {
                expectedReplayCount=Math.Max(expectedReplayCount,Math.Min(ReplayLimit,snapshot.ReplayCount));
                if(snapshot.ReplayMissing!=null)foreach(int missing in snapshot.ReplayMissing)if(missing>=0&&missing<ReplayLimit)permanentlyMissing.Add(missing);
            }
            UpdateReplayStatus();
        }

        void UpdateReplayStatus()
        {
            if(!game.NetworkShift)return;
            string status=permanentlyMissing.Count>0
                ? "回放已恢复 · "+permanentlyMissing.Count+" 段因房主断线缺失"
                : HistoryComplete() ? "共同回放已同步" : "正在补齐共同回放";
            if(permanentlyMissing.Count==0 && HistoryComplete() && room.IsHost && game.MatchFinished && !replayStarted)
                for(int i=0;i<room.Seats.Length;i++)if(room.IsRemoteSeat(i)) {status="等待搭档同步回放";break;}
            game.SetReplayStatus(status);
        }

        public CrewInput InputFor(int seat)
        {
            if (disposed || !room.IsRemoteSeat(seat) || !room.HasRemoteInput(seat)) return new CrewInput();
            InputFrame frame = room.ConsumeLatestInput(seat);
            return new CrewInput
            {
                Move = new Vector2(frame.Mx, frame.My),
                Pressed = frame.Pressed,
                Held = frame.Held,
                Cycle = frame.Cycle
            };
        }

        public void StepHost(float dt)
        {
            if (disposed || !room.IsHost || !ValidDelta(dt)) return;
            if(game.NetworkShift && game.MatchFinished)
            {
                bool remote=false;for(int i=0;i<room.Seats.Length;i++)if(room.IsRemoteSeat(i))remote=true;
                if(!remote)replayStarted=true;
                if(replayStarted)game.SetNetworkReplayClock(Math.Min(game.DisplayElapsed,game.ReplayTime+dt*12),true);
            }
            snapshotElapsed += dt;
            while (snapshotElapsed >= NetProtocol.SnapshotPeriod)
            {
                snapshotElapsed -= NetProtocol.SnapshotPeriod;
                EmitSnapshot();
            }
            UpdateReplayStatus();
        }

        public bool SampleRemote(out Snapshot a, out Snapshot b, out float t)
        {
            a = null;
            b = null;
            t = 0f;
            Snapshot snap;
            while (room.TryTakeSnapshot(out snap))
            { AcceptHistory(snap); game.Snaps.Add(snap, clock); }
            if(game.NetworkShift && room.LastAuthoritativeSnapshot!=null)
            {
                Snapshot last=room.LastAuthoritativeSnapshot;
                if(last.ReplayStarted && HistoryComplete())game.SetNetworkReplayClock(last.ReplayTime,true);
            }
            return game.Snaps != null && game.Snaps.Sample(clock, out a, out b, out t);
        }

        public void SendRemoteInput(float dt, CrewInput input, bool neutral)
        {
            if (disposed || room.IsHost || !ValidDelta(dt)) return;
            if (neutral)
            {
                input = new CrewInput();
                pendingPressed = false;
                pendingHeld = false;
                pendingCycle = false;
            }
            pendingMove = input.Move;
            pendingHeld = input.Held;
            pendingPressed |= input.Pressed;
            pendingCycle |= input.Cycle;
            inputElapsed += dt;
            while (inputElapsed >= NetProtocol.InputPeriod)
            {
                inputElapsed -= NetProtocol.InputPeriod;
                InputFrame frame = new InputFrame
                {
                    Seq = inputSeq++,
                    Mx = pendingMove.x,
                    My = pendingMove.y,
                    Pressed = pendingPressed,
                    Held = pendingHeld,
                    Cycle = pendingCycle
                };
                room.SendInput(frame);
                pendingPressed = false;
                pendingCycle = false;
            }
        }

        public void ResetPendingInput()
        {
            pendingMove = Vector2.zero;
            pendingPressed = false;
            pendingHeld = false;
            pendingCycle = false;
            inputElapsed = 0f;
        }

        void EmitSnapshot()
        {
            bool frozen=game.NetworkShift && game.MatchFinished && finalWorld!=null;
            Snapshot snapshot = frozen ? CloneSnapshot(finalWorld) : new Snapshot();
            snapshot.Tick = snapshotTick++;
            snapshot.RoundId=room.RoundId;snapshot.AuthorityEpoch=room.AuthorityEpoch;snapshot.MatchId=room.MatchId;
            snapshot.IsPractice=!game.NetworkShift;
            snapshot.Deliveries=game.Deliveries;snapshot.TrafficStops=game.TrafficStops;
            if(!frozen)
            {
            for (int i = 0; i < NetProtocol.SeatCount; i++)
            {
                Crew crew = game.Crew[i];
                snapshot.Crew[i] = new CrewSnap
                {
                    X = crew.Position.x,
                    Z = crew.Position.z,
                    RotY = crew.Visual ? crew.Visual.eulerAngles.y : 0f,
                    HeldCart = crew.Cart == null ? -1 : game.Carts.IndexOf(crew.Cart),
                    Sel = crew.Selected,
                    BlockedByTraffic = crew.BlockedByTraffic
                };
                snapshot.Labels[i] = game.ActionLabel(crew);
            }
            for (int i = 0; i < NetProtocol.CartCount; i++)
            {
                Cart cart = game.Carts[i];
                snapshot.Carts[i] = new CartSnap
                {
                    X = cart.Position.x,
                    Z = cart.Position.z,
                    RotY = cart.Visual ? cart.Visual.eulerAngles.y : 0f,
                    OwnerSeat = cart.Owner == null ? -1 : cart.Owner.Index,
                    Cargo = game.Shift != null && i < game.Shift.Carts.Length && game.Shift.Carts[i].Loaded ? 1 : 0
                };
            }
            // Ver=2 任务域镜像块：host 权威状态整块下发（sandbox 为空态）。
            if (game.Shift != null)
            {
                snapshot.Shift = game.Shift.CaptureShift();
                snapshot.Flights = game.Shift.CaptureFlights();
            }
            if (game.Sim != null)
            {
                snapshot.Score = game.Sim.Score;
                snapshot.Elapsed = game.Sim.Elapsed;
                snapshot.Ended=game.Sim.Finished;snapshot.CompletedFlights=game.Sim.DepartedCount;
                snapshot.MissedFlights=game.Sim.MissedCount;snapshot.CompletedTasks=game.Sim.CompletedTaskCount;snapshot.Stars=game.Sim.Stars;
            }
            }
            if(game.NetworkShift && game.MatchFinished && finalWorld==null)finalWorld=CloneSnapshot(snapshot);
            if (!string.Equals(lastToast, game.Toast, StringComparison.Ordinal))
            {
                lastToast = game.Toast ?? string.Empty;
                toastSeq++;
            }
            snapshot.Toast = lastToast;
            snapshot.ToastSeq = toastSeq;
            if(game.NetworkShift)
            {
                bool final=game.MatchFinished && (history.Count==0 || history[history.Count-1]==null || !history[history.Count-1].Ended);
                if(history.Count<ReplayLimit && (game.DisplayElapsed>=nextRecordAt && !game.MatchFinished || final))
                {
                    snapshot.ReplayIndex=history.Count;
                    snapshot.ReplayCount=history.Count+1;
                    nextRecordAt=game.DisplayElapsed+ReplayPeriod;
                }
                else snapshot.ReplayCount=history.Count;
                snapshot.ReplayStarted=replayStarted;snapshot.ReplayTime=game.ReplayTime;
                snapshot.ReplayMissing=new List<int>(permanentlyMissing).ToArray();Array.Sort(snapshot.ReplayMissing);
                snapshot.ReplayIncomplete=permanentlyMissing.Count>0;
                if(snapshot.ReplayIndex>=0) {history.Add(snapshot);expectedReplayCount=history.Count;game.RecordNetworkSnapshot(snapshot);}
            }
            room.BroadcastSnapshot(snapshot);
        }

        static Snapshot CloneSnapshot(Snapshot snapshot)
        {
            return NetProtocol.DecodeSnapshot(NetProtocol.Parse(NetProtocol.BuildSnapshot(snapshot)));
        }

        void RefreshCrewNames()
        {
            for (int i = 0; i < game.Crew.Count && i < room.Seats.Length; i++)
                if (!string.IsNullOrEmpty(room.Seats[i].Name)) game.Crew[i].Name = room.Seats[i].Name;
        }

        void ReturnToLobby(string message)
        {
            if (disposed) return;
            disposed = true;
            lobbyToast = message;
            AppState state = AppState.Ensure();
            if (state.Room != null) state.Room.Leave();
            if (state.Beacon != null) state.Beacon.Stop();
            state.Launch.Reset();
            game.Paused = false;
            game.Help = false;
            game.Started = false;
            SceneManager.LoadScene(AppState.SceneCabinLobby);
        }

        static bool ValidDelta(float dt)
        {
            return dt > 0f && !float.IsNaN(dt) && !float.IsInfinity(dt);
        }

        void UpdateBeacon()
        {
            AppState state = AppState.Ensure();
            if (state.Beacon == null) return;
            if (!room.IsHost || room.Phase == RoomPhase.Idle || room.Phase == RoomPhase.Closed)
            {
                if (state.Beacon.Announcing) state.Beacon.Stop();
                return;
            }
            if (!state.Beacon.Announcing) state.Beacon.Start(new RoomInfo());
            RoomInfo beacon = state.Beacon.Room;
            beacon.Name = room.RoomName;
            int authoritySeat = room.LocalSeat;
            beacon.HostName = authoritySeat < 0 || authoritySeat >= room.Seats.Length ? string.Empty : room.Seats[authoritySeat].Name;
            beacon.State = room.BeaconState;
            beacon.Port = NetProtocol.SessionPort;
            beacon.Seats = NetProtocol.SeatCount;
            beacon.Taken = room.TakenCount;
        }
    }
}
