using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using IslandAirport;

public static partial class NetPlaytest
{
    const int FuelCartId = (int)ServiceKind.Fuel;
    static Flight fuelFlight;
    static string fuelFlightId = string.Empty;
    static Vector3[] fuelDrivePoints;
    static int fuelDriveWaypoint;
    static Vector3 fuelDriveGoal;
    static Vector3 fuelDriveStart;
    static bool fuelContestPressSent;
    static bool fuelContestReleaseFrameSent;
    static bool fuelContestClosePressSent;
    static bool fuelHostMountPressSent;
    static bool fuelHostNozzleExclusiveSent;
    static bool fuelDriveRestrictionPending;
    static Vector3 fuelDriveRestrictionStart;
    static bool fuelHostFillCaptured;
    static bool fuelHostAircraftFillCaptured;
    static bool fuelHostSpillCaptured;
    static bool fuelHostStationCleanupReady;
    static bool fuelHostDeliveryCaptured;
    static bool fuelClientStationFillCaptured;
    static bool fuelClientAircraftFillCaptured;
    static bool fuelClientStationSpillCaptured;
    static bool fuelClientSpillConfirmationSent;
    static bool fuelClientDeliveryCaptured;
    static bool fuelClientPressSent;
    static bool fuelClientValveActionSent;
    static bool fuelClientDetachSent;
    static bool fuelClientConflictSpillSeen;
    static bool fuelClientConflictCleaning;
    static bool fuelClientCleaning;
    static float fuelCancelProgress;
    static float fuelCancelElapsed;
    static float fuelCancelSampleProgress;
    static float fuelCancelSampleTank;
    static float fuelCancelSampleElapsed;
    static bool fuelCancelStableSampled;
    static int fuelScoreAtSpill;
    static int fuelScoreAtDelivery;
    static bool fuelHostGraceObserved;
    static bool fuelHostDetachVerified;
    static bool fuelClientHoseTakeSent;
    static bool fuelClientAttachSent;
    static bool fuelClientDockCleaning;

    public static void RunHostFuel()
    {
        ResetFuelScenario();
        StartRun("host-fuel");
    }

    public static void RunClientFuel()
    {
        ResetFuelScenario();
        StartRun("client-fuel");
    }

    static void ResetFuelScenario()
    {
        fuelFlight = null;
        fuelFlightId = string.Empty;
        fuelDrivePoints = null;
        fuelDriveWaypoint = 0;
        fuelDriveGoal = Vector3.zero;
        fuelDriveStart = Vector3.zero;
        fuelContestPressSent = false;
        fuelContestReleaseFrameSent = false;
        fuelContestClosePressSent = false;
        fuelHostMountPressSent = false;
        fuelHostNozzleExclusiveSent = false;
        fuelDriveRestrictionPending = false;
        fuelDriveRestrictionStart = Vector3.zero;
        fuelHostFillCaptured = false;
        fuelHostAircraftFillCaptured = false;
        fuelHostSpillCaptured = false;
        fuelHostStationCleanupReady = false;
        fuelHostDeliveryCaptured = false;
        fuelClientStationFillCaptured = false;
        fuelClientAircraftFillCaptured = false;
        fuelClientStationSpillCaptured = false;
        fuelClientSpillConfirmationSent = false;
        fuelClientDeliveryCaptured = false;
        fuelClientPressSent = false;
        fuelClientValveActionSent = false;
        fuelClientDetachSent = false;
        fuelClientConflictSpillSeen = false;
        fuelClientConflictCleaning = false;
        fuelClientCleaning = false;
        fuelCancelProgress = 0f;
        fuelCancelElapsed = 0f;
        fuelCancelSampleProgress = 0f;
        fuelCancelSampleTank = 0f;
        fuelCancelSampleElapsed = 0f;
        fuelCancelStableSampled = false;
        fuelScoreAtSpill = 0;
        fuelScoreAtDelivery = 0;
        fuelHostGraceObserved = false;
        fuelHostDetachVerified = false;
        fuelClientHoseTakeSent = false;
        fuelClientAttachSent = false;
        fuelClientDockCleaning = false;
    }

    static void AdvanceHostFuel(double now, float dt)
    {
        if (stage == 0)
        {
            Require(SceneManager.GetActiveScene().name == AppState.SceneCabinLobby, "Fuel host did not start in CabinLobby.");
            Require(state.Room.SocketFactory == null, "Fuel host RoomManager is not using its real UDP socket factory.");
            Require(state.CanPlayWithOthers, "Fuel host authentication did not pass multiplayer eligibility.");
            Require(state.Room.Host("NetPlay Fuel", "NetplayHost5", FakeAuthToken()), "Fuel Room.Host failed.");
            Signal("s5-host-ready");
            report.AppendLine("PASS Session 5 host bound a real UDP socket for the fuel scenario.");
            SetStage(1, now);
            return;
        }

        if (stage == 1)
        {
            if (!state.Room.Seats[1].Occupied) return;
            LobbyApp lobby = UnityEngine.Object.FindObjectOfType<LobbyApp>();
            Require(lobby != null, "Fuel CabinLobby LobbyApp was not available to start the room.");
            lobby.BeginPracticeShift();
            Require(state.Launch.Mode == AppState.GameMode.HostSandbox, "Fuel lobby did not launch HostSandbox.");
            SetStage(2, now);
            return;
        }

        if (stage == 2)
        {
            if (!game || !game.Started) return;
            Require(SceneManager.GetActiveScene().name == AppState.ScenePalmBay, "Fuel host did not load PalmBay.");
            Require(game.Sandbox && !game.Remote && game.Sim != null && game.Sim.Endless,
                "Fuel host scene did not enter endless practice mode.");
            if (!TryFindReadyFuelFlight(out fuelFlight)) return;
            fuelFlightId = fuelFlight.Id;
            Require(fuelFlight.Progress[FuelCartId] == 0f, "Fuel fixture flight already has fuel progress.");
            Require(game.Shift.NozzleState == NozzlePhase.AtStation && game.Shift.NozzleSeat == -1,
                "Fuel session did not begin with the shared nozzle on its station rack.");
            Require(game.Shift.FuelTruckTank <= 0.001f, "Fuel session began with a non-empty truck tank.");
            Require(game.Shift.HoseState == HosePhase.OnTruck && !game.Shift.ValveOpen,
                "Fuel session did not begin with the truck hose reeled and the valve closed.");
            ConfigureFuelFixtures(fuelFlight);
            File.WriteAllText(Path.Combine(controlDirectory, "s5-flight-id"), fuelFlight.Id);
            Signal("s5-host-fuel-ready");
            report.AppendLine("PASS Practice scheduler supplied flight " + fuelFlight.Id + " at stand " + fuelFlight.Stand +
                "; fixtures placed both crew near the station and the fuel truck on its service road.");
            SetStage(3, now);
            return;
        }

        if (stage == 3)
        {
            if (!HasSignal("s5-client-nozzle-held")) return;
            if (game.Shift.NozzleState != NozzlePhase.Held || game.Shift.NozzleSeat != 1) return;
            Require(game.Shift.FuelTruckAtStation,
                "Fuel truck fixture fell outside the runtime 3.4m station hookup guard.");
            if (!fuelContestPressSent)
            {
                Require(game.Shift.CanDriveCart(FuelCartId) && game.Shift.HoseState == HosePhase.OnTruck,
                    "M3.3r: holding the station nozzle alone must not lock the truck (only valve/inserted nozzle/hose out do).");
                game.SetInput(0, new CrewInput { Pressed = true, Held = true });
                stepOverride = PickupProbeDelta;
                fuelContestPressSent = true;
                report.AppendLine("Probe: host seat 0 pressed at the fuel rack while client seat 1 held the single nozzle.");
                return;
            }

            Require(game.Shift.NozzleState == NozzlePhase.Held && game.Shift.NozzleSeat == 1,
                "The shared fuel nozzle changed ownership after the host pressed while the client held it.");
            if (!fuelContestClosePressSent)
            {
                Require(game.Shift.ValveOpen,
                    "Host rack-zone Pressed input did not follow the valve action when another seat held the nozzle.");
                Require(game.Carts[FuelCartId].Owner == null && game.Crew[0].Cart == null,
                    "The host mounted the fuel truck during the shared-nozzle ownership probe.");
                if (!fuelContestReleaseFrameSent)
                {
                    game.SetInput(0, new CrewInput());
                    fuelContestReleaseFrameSent = true;
                    return;
                }
                game.SetInput(0, new CrewInput { Pressed = true, Held = true });
                stepOverride = PickupProbeDelta;
                fuelContestClosePressSent = true;
                report.AppendLine("PASS Client retained the sole nozzle after the host rack Pressed input; the input routed to the valve without transferring ownership.");
                return;
            }

            if (game.Shift.ValveOpen) return;
            if (!fuelHostNozzleExclusiveSent)
            {
                Require(game.Shift.NozzleState == NozzlePhase.Held && game.Shift.NozzleSeat == 1,
                    "Closing the valve must not auto-return a nozzle that is in a player's hand.");
                Require(game.Shift.Spills.Count == 1,
                    "The station-zone contest did not produce the expected single spill before cleanup.");
                Signal("s5-host-nozzle-exclusive");
                fuelHostNozzleExclusiveSent = true;
            }
            if (!HasSignal("s5-client-contest-spill-cleared")) return;
            Require(game.Shift.NozzleState == NozzlePhase.Held && game.Shift.NozzleSeat == 1 &&
                game.Shift.Spills.Count == 0,
                "Client spill cleanup changed nozzle ownership or left the contest spill behind.");
            report.AppendLine("PASS Client cleaned the contest spill on foot; the host and client still shared the same single held nozzle.");
            SetStage(4, now);
            return;
        }

        if (stage == 4)
        {
            if (game.Shift.NozzleState != NozzlePhase.OnTruck) return;
            Require(game.Crew[1].Cart == null && game.Carts[FuelCartId].Owner == null,
                "Client attached the nozzle while riding the truck; filling must be performed on foot.");
            Require(game.Shift.NozzleSeat == -1 && !game.Shift.CanDriveCart(FuelCartId),
                "The fuel truck remained driveable while the station nozzle was inserted.");
            report.AppendLine("PASS Client tapped the parked fuel truck and inserted the station nozzle while on foot.");
            SetStage(5, now);
            return;
        }

        if (stage == 5)
        {
            if (!game.Shift.ValveOpen || game.Shift.FuelTruckTank < 0.25f) return;
            if (!fuelHostFillCaptured)
            {
                Require(game.Shift.NozzleState == NozzlePhase.OnTruck,
                    "Truck tank rose without the nozzle connected to the fuel truck.");
                Require(game.Shift.FuelTruckTank < 0.95f && game.Shift.Spills.Count == 0,
                    "Host missed the automatic-fill screenshot window before the full-tank spill.");
                Shot("Evidence/m3-fuel-fill-host.png", 1920, 1080);
                report.AppendLine("PASS Host captured automatic truck filling with the valve open and client input neutral.");
                Signal("s5-host-fill-captured");
                fuelHostFillCaptured = true;
                return;
            }

            if (game.Shift.FuelTruckTank >= 0.999f && game.Shift.Spills.Count == 0 && !fuelHostGraceObserved &&
                game.Shift.StationOverfill > 0f && game.Shift.StationOverfill < ShiftSim.OverfillGraceSeconds)
            {
                fuelHostGraceObserved = true;
                report.AppendLine("PASS Full truck with the valve open stayed spill-free during the overfill grace (" +
                    game.Shift.StationOverfill.ToString("0.00") + "s < " + ShiftSim.OverfillGraceSeconds.ToString("0.0") + "s).");
            }

            if (!fuelHostSpillCaptured && HasSignal("s5-client-station-spill-captured") &&
                game.Shift.FuelTruckTank >= 0.999f && game.Shift.Spills.Count > 0)
            {
                Require(game.Shift.StationOverfill >= ShiftSim.OverfillGraceSeconds && game.Shift.StationSpilling,
                    "Full-tank spill started before the 2 s overfill grace elapsed.");
                fuelScoreAtSpill = game.Sim.Score;
                Shot("Evidence/m3-fuel-spill-host.png", 1920, 1080);
                report.AppendLine("PASS Host captured full-tank overflow after the 2 s grace with the station valve still open.");
                Signal("s5-host-spill-captured");
                fuelHostSpillCaptured = true;
                return;
            }

            if (fuelHostSpillCaptured && HasSignal("s5-client-spill-captured"))
            {
                Signal("s5-close-valve");
                SetStage(6, now);
            }
            return;
        }

        if (stage == 6)
        {
            if (game.Shift.ValveOpen) return;
            Require(game.Shift.NozzleState == NozzlePhase.AtStation,
                "M3.3r: closing the valve did not auto-return the inserted station nozzle.");
            Require(game.Shift.FuelTruckTank >= 0.999f && game.Shift.FuelTruckTank <= 1f,
                "The truck tank did not reach full capacity before station handoff.");
            if (!fuelHostStationCleanupReady)
            {
                Require(game.Shift.Spills.Count == 1,
                    "Full tank with the valve left open did not create exactly one spill zone.");
                Signal("s5-host-station-cleanup-ready");
                fuelHostStationCleanupReady = true;
                return;
            }
            if (!HasSignal("s5-client-spill-cleared")) return;
            Require(game.Shift.Spills.Count == 0 && !game.Shift.Spilling,
                "Client did not clear the full-tank spill after closing the valve.");
            Require(game.Sim.Score == fuelScoreAtSpill, "The spill or its cleanup unexpectedly changed the task score.");
            report.AppendLine("PASS Client closed the valve with one tap (nozzle auto-returned) and cleared the full-tank spill on foot; full truck fuel remained available with no penalty.");
            SetStage(7, now);
            return;
        }

        if (stage == 7)
        {
            Require(!game.Shift.ValveOpen && game.Shift.NozzleState == NozzlePhase.AtStation &&
                game.Shift.HoseState == HosePhase.OnTruck && game.Shift.CanDriveCart(FuelCartId),
                "The fuel truck was not driveable after the valve closed and the nozzle auto-returned.");
            Require(game.Carts[FuelCartId].Owner == null,
                "The fuel truck was already owned before the host handoff.");
            report.AppendLine("PASS No walk-back stow step: valve closed, nozzle home, truck hose reeled, truck driveable.");
            Signal("s5-host-ready-drive");
            SetStage(8, now);
            return;
        }

        if (stage == 8)
        {
            if (game.Crew[0].Cart == null)
            {
                Vector3 delta = game.Carts[FuelCartId].Position - game.Crew[0].Position;
                delta.y = 0f;
                if (delta.magnitude > 0.8f)
                {
                    game.SetInput(0, new CrewInput { Move = new Vector2(delta.x, delta.z).normalized });
                    return;
                }
                if (!fuelHostMountPressSent)
                {
                    game.SetInput(0, new CrewInput { Pressed = true, Held = true });
                    stepOverride = PickupProbeDelta;
                    fuelHostMountPressSent = true;
                }
                return;
            }
            Require(game.Crew[0].Cart == game.Carts[FuelCartId] && game.Carts[FuelCartId].Owner == game.Crew[0],
                "Host could not take over the fuel truck through an AirportGame Pressed input.");
            Require(game.Shift.CanDriveCart(FuelCartId), "Host took the truck while its valve/nozzle/hose state disallowed driving.");
            Require(game.Shift.NozzleState == NozzlePhase.AtStation, "The station nozzle must stay at the station when the truck leaves.");
            fuelDriveStart = game.Carts[FuelCartId].Position;
            fuelDriveGoal = AirportGame.Dock(fuelFlight.Stand) + new Vector3(0.4f, 0f, 0.4f);
            fuelDrivePoints = BuildFuelDrivePoints(fuelDriveStart, fuelDriveGoal);
            fuelDriveWaypoint = 0;
            report.AppendLine("PASS Host single tap took the truck after the client finished station work; planning the real drive to stand " +
                fuelFlight.Stand + ".");
            Signal("s5-host-driving-started");
            SetStage(9, now);
            return;
        }

        if (stage == 9)
        {
            DriveFuelTruckTowardDock(now);
            return;
        }

        if (stage == 10)
        {
            if (fuelDriveRestrictionPending)
            {
                fuelDriveRestrictionPending = false;
                Require(Vector3.Distance(game.Carts[FuelCartId].Position, fuelDriveRestrictionStart) < 0.005f,
                    "The host moved the fuel truck while its hose was connected to an aircraft.");
                Require(game.Shift.HoseState == HosePhase.OnAircraft && game.Shift.HoseFlightId == fuelFlightId,
                    "The host movement attempt changed the aircraft hose connection.");
                report.AppendLine("PASS Host Move input was blocked while the truck hose was connected; CanDriveCart remained false.");
                Signal("s5-host-drive-restricted");
                SetStage(11, now);
                return;
            }

            if (game.Shift.HoseState != HosePhase.OnAircraft || game.Shift.HoseFlightId != fuelFlightId) return;
            Require(game.Shift.HoseSeat == 1, "Truck hose connection is not attributed to the client seat.");
            Require(game.Crew[0].Cart == game.Carts[FuelCartId],
                "Host lost the driver seat before the connected-hose driving restriction check.");
            Require(!game.Shift.CanDriveCart(FuelCartId),
                "The fuel truck remained driveable with its hose connected to the aircraft.");
            Vector2 move = FuelTruckMoveTowardStation();
            Require(move.sqrMagnitude > 0.5f, "Could not find a legal direction for the connected-hose drive probe.");
            fuelDriveRestrictionStart = game.Carts[FuelCartId].Position;
            game.SetInput(0, new CrewInput { Move = move });
            stepOverride = PickupProbeDelta;
            fuelDriveRestrictionPending = true;
            return;
        }

        if (stage == 11)
        {
            if (!fuelHostAircraftFillCaptured)
            {
                if (fuelFlight.Progress[FuelCartId] < 0.25f) return;
                Shot("Evidence/m3-fuel-transfer-host.png", 1920, 1080);
                report.AppendLine("PASS Host captured the automatic aircraft fuel transfer with no held client input.");
                Signal("s5-host-aircraft-fill-captured");
                fuelHostAircraftFillCaptured = true;
                return;
            }

            if (!HasSignal("s5-client-fill-cancelled")) return;
            float progress = fuelFlight.Progress[FuelCartId];
            Require(progress >= 0.20f && progress < 0.99f,
                "Disconnecting mid-transfer lost or completed the partial progress: " + progress.ToString("0.000") + ".");
            Require(Mathf.Abs(game.Shift.FuelTruckTank + progress - 1f) < 0.035f,
                "Disconnecting did not preserve the 1:1 truck-tank / flight-progress transfer.");
            Require(game.Shift.HoseState == HosePhase.OnTruck,
                "Second aircraft tap did not disconnect and auto-reel the hose.");
            report.AppendLine("PASS Client tapped the aircraft again at " + progress.ToString("0.00") +
                "; the hose auto-reeled and both truck fuel and flight progress remained conserved and resumable.");
            Signal("s5-resume-fill");
            SetStage(12, now);
            return;
        }

        if (stage == 12)
        {
            if (fuelFlight.Progress[FuelCartId] < 1f || game.Shift.FuelTruckTank > 0.01f) return;
            Require(game.Shift.HoseState == HosePhase.OnAircraft && game.Shift.HoseFlightId == fuelFlightId,
                "Completed fueling did not leave the hose connected for an explicit disconnect.");
            fuelScoreAtDelivery = game.Sim.Score;
            Shot("Evidence/m3-fuel-deliver-host.png", 1920, 1080);
            report.AppendLine("PASS Host captured completed fuel delivery; the aircraft reached 100% and the truck tank was consumed.");
            Signal("s5-host-deliver-captured");
            fuelHostDeliveryCaptured = true;
            SetStage(13, now);
            return;
        }

        if (stage == 13)
        {
            if (!fuelHostDeliveryCaptured || !HasSignal("s5-client-deliver-captured")) return;
            if (!game.Shift.HoseSpilling) return;
            Require(game.Shift.HoseOverfill >= ShiftSim.OverfillGraceSeconds && game.Shift.Spills.Count == 1,
                "Aircraft-full overfill spill started before the 2 s grace or did not create exactly one spill.");
            Vector3 dock = AirportGame.Dock(fuelFlight.Stand);
            ShiftSpill dockSpill = game.Shift.Spills[0];
            Require(Vector2.Distance(new Vector2(dockSpill.X, dockSpill.Z), new Vector2(dock.x, dock.z)) < 0.01f,
                "Aircraft overfill spill did not appear at the aircraft's service bay.");
            Require(game.Sim.Score == fuelScoreAtDelivery && !game.Shift.IsTaskFailed(fuelFlightId, ServiceKind.Fuel),
                "Aircraft overfill spill changed score or failed the fuel task.");
            report.AppendLine("PASS Full aircraft with the hose still connected spilled at its stand only after the 2 s grace; no score or task penalty.");
            Signal("s5-client-detach-hose");
            SetStage(14, now);
            return;
        }

        if (stage == 14)
        {
            if (!fuelHostDetachVerified)
            {
                if (game.Shift.HoseState != HosePhase.OnTruck || game.Shift.Spilling) return;
                Require(fuelFlight.Progress[FuelCartId] >= 1f,
                    "Disconnecting the hose changed completed aircraft fuel progress.");
                Require(game.Shift.HoseSeat == -1 && game.Shift.CanDriveCart(FuelCartId),
                    "Disconnected hose did not auto-reel onto a driveable fuel truck.");
                fuelHostDetachVerified = true;
                report.AppendLine("PASS Client's single aircraft tap disconnected first even while standing on the oil; overfill stopped and the truck became driveable.");
                return;
            }
            if (!HasSignal("s5-client-dock-spill-cleared") || game.Shift.Spills.Count != 0) return;
            report.AppendLine("PASS Client held cleanup on foot and removed the stand overfill spill.");
            state.Room.CloseRoom();
            report.AppendLine("PASS Session 5 host sent the real close control after fuel and spill checks.");
            Signal("s5-host-close-sent");
            SetStage(15, now);
            return;
        }

        if (stage == 15)
        {
            if (!HasSignal("s5-client-returned")) return;
            Complete(false, "Session 5 (M3.3r) verified single-tap nozzle pickup/insert, automatic truck filling, valve-close auto-return, single-nozzle exclusion, station overfill grace, host driving handoff, truck-hose take/attach with automatic transfer, connected-hose drive lock, disconnect/resume, delivery, stand overfill grace and spill cleanup over real AirportGame inputs.");
        }
    }

    static void ConfigureFuelFixtures(Flight flight)
    {
        Vector3 station = AirportGame.Station(ServiceKind.Fuel);
        Vector3 truckPosition = Level1Map.CartPark(ServiceKind.Fuel);
        float stationToTruck = Vector3.Distance(station, truckPosition);
        Require(stationToTruck > 3.1f && stationToTruck < 3.4f,
            "Fuel truck park must keep the 1.2m valve and 1.9m truck player interaction zones separate while remaining inside the 3.4m station hookup range.");
        for (int i = 0; i < game.Carts.Count; i++)
        {
            Cart cart = game.Carts[i];
            cart.Owner = null;
            if (cart.Kind == ServiceKind.Fuel)
            {
                cart.Position = truckPosition;
                cart.Visual.position = truckPosition;
            }
            else
            {
                // Park the unrelated carts in their legal branch-road bays,
                // leaving the north/south service spine open for the fuel drive.
                cart.Position = Level1Map.Station(cart.Kind) + new Vector3(0.36f, 0f, 0f);
                cart.Visual.position = cart.Position;
            }
            Require(Level1Map.InDrivable(cart.Position, 0.35f),
                "Fuel fixture placed " + cart.Kind + " outside a legal vehicle footprint.");
        }

        game.Crew[0].Cart = null;
        game.Crew[1].Cart = null;
        game.Crew[0].Selected = game.Sim.Flights.IndexOf(flight);
        game.Crew[1].Selected = game.Sim.Flights.IndexOf(flight);
        SetCrewPosition(game.Crew[0], station + new Vector3(-0.35f, 0f, 0.3f));
        SetCrewPosition(game.Crew[1], station);
    }

    static bool TryFindReadyFuelFlight(out Flight result)
    {
        result = null;
        if (game == null || game.Sim == null) return false;
        for (int i = 0; i < game.Sim.Flights.Count; i++)
        {
            Flight candidate = game.Sim.Flights[i];
            if (candidate == null || candidate.Status != FlightStatus.Servicing ||
                !game.StandReady(candidate.Stand) || candidate.Progress[FuelCartId] >= 1f) continue;
            result = candidate;
            return true;
        }
        return false;
    }

    static void SetCrewPosition(Crew crew, Vector3 position)
    {
        crew.Position = position;
        if (crew.Visual) crew.Visual.position = position;
    }

    static Vector3[] BuildFuelDrivePoints(Vector3 from, Vector3 to)
    {
        var parked = new List<Vector3>();
        for (int i = 0; i < game.Carts.Count; i++)
            if (i != FuelCartId) parked.Add(game.Carts[i].Position);
        Vector3[] route = Level1Map.DriveRoute(from, to, parked);
        Require(route != null, "No drivable road route exists from the fuel station to stand " + fuelFlight.Stand + ".");
        var points = new Vector3[route.Length + 1];
        for (int i = 0; i < route.Length; i++) points[i] = route[i];
        points[route.Length] = to;
        return points;
    }

    static void DriveFuelTruckTowardDock(double now)
    {
        Cart truck = game.Carts[FuelCartId];
        Require(game.Crew[0].Cart == truck && truck.Owner == game.Crew[0],
            "Host lost control of the fuel truck during the drive to the aircraft.");
        Require(game.Shift.CanDriveCart(FuelCartId), "Fuel truck drive permission was revoked before reaching the dock.");
        while (fuelDriveWaypoint < fuelDrivePoints.Length &&
            Vector3.Distance(truck.Position, fuelDrivePoints[fuelDriveWaypoint]) < 0.01f)
            fuelDriveWaypoint++;

        if (fuelDriveWaypoint >= fuelDrivePoints.Length)
        {
            Require(Vector3.Distance(truck.Position, fuelDriveGoal) < 1.65f,
                "Host drive ended outside the target fuel dock interaction radius.");
            Require(Vector3.Distance(fuelDriveStart, truck.Position) > 2f,
                "Host fuel truck did not actually move during the handoff drive.");
            report.AppendLine("PASS Host drove the fuel truck from the station to stand " + fuelFlight.Stand +
                " through AirportGame.Move; traveled " + Vector3.Distance(fuelDriveStart, truck.Position).ToString("0.0") + "m.");
            Signal("s5-host-at-dock");
            SetStage(10, EditorApplication.timeSinceStartup);
            return;
        }

        Vector3 delta = fuelDrivePoints[fuelDriveWaypoint] - truck.Position;
        delta.y = 0f;
        if (delta.sqrMagnitude < 0.0001f) return;
        Vector2 move = new Vector2(delta.x, delta.z).normalized;
        game.SetInput(0, new CrewInput { Move = move });
        // Reach each planner corner before turning: cutting a narrow road's
        // corner can wedge against ClampToDrivable even though the route is legal.
        stepOverride = Mathf.Min(0.04f, delta.magnitude / 3.6f);
    }

    static Vector2 FuelTruckMoveTowardStation()
    {
        Vector3 current = game.Carts[FuelCartId].Position;
        Vector3[] route = Level1Map.DriveRoute(current, AirportGame.Station(ServiceKind.Fuel));
        Require(route != null, "No valid drive-probe route exists from the fuel dock.");
        Vector3 target = route.Length == 0 ? AirportGame.Station(ServiceKind.Fuel) : route[0];
        Vector3 delta = target - current;
        delta.y = 0f;
        return delta.sqrMagnitude < 0.0001f ? Vector2.zero : new Vector2(delta.x, delta.z).normalized;
    }

    static void AdvanceClientFuel(double now, float dt)
    {
        if (stage >= 2 && stage < 15 && !string.IsNullOrEmpty(fuelFlightId))
        {
            fuelFlight = FindMirrorFlight(fuelFlightId);
            if (fuelFlight == null) return;
        }

        if (stage == 0)
        {
            Require(state.Room.SocketFactory == null, "Fuel client RoomManager is not using its real UDP socket factory.");
            Require(state.CanPlayWithOthers, "Fuel client authentication did not pass multiplayer eligibility.");
            Require(state.Room.Join(joinAddress, "NetplayClient5", FakeAuthToken()), "Fuel Room.Join failed for " + joinAddress + ".");
            state.Room.RoomName = "NetPlay Fuel";
            report.AppendLine("PASS Session 5 client called the real Room.Join for " + joinAddress + ".");
            SetClientFuelStage(1, now);
            return;
        }

        if (stage == 1)
        {
            if (state.Room.Phase != RoomPhase.Starting && state.Room.Phase != RoomPhase.Playing) return;
            if (!game || !game.Started || !game.Remote || game.Snaps == null || game.Snaps.Count < 2) return;
            if (!HasSignal("s5-host-fuel-ready")) return;
            Require(game.RemoteSeat == 1 && !game.Sandbox && game.Shift != null && game.Sim != null && game.Sim.Endless,
                "Fuel client did not start as the read-only seat 1 practice mirror.");
            string idPath = Path.Combine(controlDirectory, "s5-flight-id");
            if (!File.Exists(idPath)) return;
            fuelFlightId = File.ReadAllText(idPath).Trim();
            fuelFlight = FindMirrorFlight(fuelFlightId);
            if (fuelFlight == null || !game.StandReady(fuelFlight.Stand)) return;
            // The file signal can precede the UDP fixture snapshots. The buffer
            // may already contain spawn snapshots, and crew positions interpolate
            // behind their arrival. Wait for the actual mirrored fixture before
            // asserting its initial state or sending the first interaction.
            Vector3 station = AirportGame.Station(ServiceKind.Fuel);
            if (Vector3.Distance(game.Crew[0].Position, station + new Vector3(-0.35f, 0f, 0.3f)) > 0.05f ||
                Vector3.Distance(game.Crew[1].Position, station) > 0.05f ||
                Vector3.Distance(game.Carts[FuelCartId].Position, Level1Map.CartPark(ServiceKind.Fuel)) > 0.05f)
                return;
            Require(fuelFlight.Progress[FuelCartId] == 0f,
                "Fuel client mirror did not start with zero progress for " + fuelFlightId + ".");
            Require(game.Shift.NozzleState == NozzlePhase.AtStation,
                "Fuel client mirror did not show the shared nozzle on the station rack.");
            Require(Vector3.Distance(game.Crew[1].Position, AirportGame.Station(ServiceKind.Fuel)) < 1.5f,
                "Fuel host did not place the client at the starting fuel station fixture.");
            report.AppendLine("PASS Client mirror shows flight " + fuelFlightId + " at stand " + fuelFlight.Stand +
                " and the single nozzle at the fuel station.");
            SetClientFuelStage(2, now);
            return;
        }

        if (stage == 2)
        {
            if (game.Shift.NozzleState == NozzlePhase.Held && game.Shift.NozzleSeat == 1)
            {
                Require(game.Shift.CanDriveCart(FuelCartId) && game.Shift.HoseState == HosePhase.OnTruck,
                    "Client mirror: holding the station nozzle alone must not lock the truck (M3.3r).");
                report.AppendLine("PASS Client single tap picked up the station nozzle through Remote UDP.");
                Signal("s5-client-nozzle-held");
                SetClientFuelStage(3, now);
                return;
            }
            if (!fuelClientPressSent)
            {
                Require(game.Crew[1].Cart == null, "Client must be on foot to take the station nozzle.");
                Require(Vector3.Distance(game.Crew[1].Position, AirportGame.Station(ServiceKind.Fuel)) < 1.2f,
                    "Client is outside the nozzle station interaction radius.");
                SendClientFuelPress();
                fuelClientPressSent = true;
                report.AppendLine("Probe: one client Pressed=true input sent at the fuel nozzle rack.");
            }
            else game.SetInput(0, new CrewInput());
            return;
        }

        if (stage == 3)
        {
            if (!HasSignal("s5-host-nozzle-exclusive")) return;
            if (!fuelClientConflictSpillSeen)
            {
                if (game.Shift.ValveOpen || game.Shift.Spills.Count == 0) return;
                Require(game.Shift.Spills.Count == 1,
                    "Client mirror did not show exactly one station spill after the host ownership probe.");
                fuelClientConflictSpillSeen = true;
                report.AppendLine("PASS Client mirror received the host ownership probe and its single station spill.");
            }
            if (game.Shift.Spills.Count > 0)
            {
                ShiftSpill conflictSpill = game.Shift.Spills[0];
                Vector3 conflictPoint = new Vector3(conflictSpill.X, game.Crew[1].Position.y, conflictSpill.Z);
                if (!fuelClientConflictCleaning)
                {
                    if (!WalkClientFuelToward(conflictPoint, 0.18f)) return;
                    game.SetInput(0, new CrewInput { Held = true });
                    fuelClientConflictCleaning = true;
                    return;
                }
                game.SetInput(0, new CrewInput { Held = true });
                return;
            }
            if (fuelClientConflictCleaning && !HasSignal("s5-client-contest-spill-cleared"))
            {
                game.SetInput(0, new CrewInput());
                signalAfterStep = "s5-client-contest-spill-cleared";
                fuelClientConflictCleaning = false;
                return;
            }
            if (!HasSignal("s5-client-contest-spill-cleared")) return;
            if (game.Shift.NozzleState == NozzlePhase.OnTruck)
            {
                Require(game.Crew[1].Cart == null, "Client attached the nozzle while riding the fuel truck.");
                Require(game.Shift.NozzleSeat == -1 && !game.Shift.CanDriveCart(FuelCartId),
                    "Client mirror allowed fuel truck driving while the station nozzle was inserted.");
                report.AppendLine("PASS Client on foot tapped the parked fuel truck and inserted the nozzle.");
                SetClientFuelStage(4, now);
                return;
            }

            Vector3 truck = game.Carts[FuelCartId].Position;
            if (!WalkClientFuelToward(truck, 0.8f)) return;
            if (!fuelClientPressSent)
            {
                SendClientFuelPress();
                fuelClientPressSent = true;
                return;
            }
            game.SetInput(0, new CrewInput());
            return;
        }

        if (stage == 4)
        {
            if (game.Shift.ValveOpen)
            {
                Require(game.Shift.NozzleState == NozzlePhase.OnTruck,
                    "Station valve opened without a nozzle attached to the truck.");
                Require(!game.Shift.CanDriveCart(FuelCartId),
                    "The client mirror allowed fuel truck driving during station fueling.");
                report.AppendLine("PASS Client walked back to the station and tapped the valve open; truck filling now proceeds without held input.");
                SetClientFuelStage(5, now);
                return;
            }
            if (!WalkClientFuelToward(AirportGame.Station(ServiceKind.Fuel), 0.25f)) return;
            if (!fuelClientValveActionSent)
            {
                Require(game.Crew[1].Cart == null, "Client must be on foot at the station to open the fuel valve.");
                SendClientFuelPress();
                fuelClientValveActionSent = true;
            }
            else game.SetInput(0, new CrewInput());
            return;
        }

        if (stage == 5)
        {
            // Neutral Remote frames prove the valve fills the truck without a held pump input.
            game.SetInput(0, new CrewInput());
            if (!fuelClientStationFillCaptured && HasSignal("s5-host-fill-captured") &&
                game.Shift.ValveOpen && game.Shift.FuelTruckTank >= 0.25f &&
                game.Shift.FuelTruckTank < 0.95f && game.Shift.Spills.Count == 0)
            {
                Shot("Evidence/m3-fuel-fill-client.png", 1920, 1080);
                report.AppendLine("PASS Client captured the same automatic station fill from its read-only mirror.");
                Signal("s5-client-station-fill-captured");
                fuelClientStationFillCaptured = true;
                return;
            }

            if (!fuelClientStationFillCaptured && HasSignal("s5-host-fill-captured") &&
                (game.Shift.FuelTruckTank >= 0.95f || game.Shift.Spills.Count > 0))
                throw new TimeoutException("Client mirror missed the partial-fill capture window before the full-tank spill.");

            if (!fuelClientStationSpillCaptured && game.Shift.FuelTruckTank >= 0.999f && game.Shift.Spills.Count > 0)
            {
                Require(game.Shift.StationOverfill >= ShiftSim.OverfillGraceSeconds,
                    "Client mirror shows a full-tank spill before the mirrored 2 s overfill grace.");
                Shot("Evidence/m3-fuel-spill-client.png", 1920, 1080);
                report.AppendLine("PASS Client mirror captured the full-tank spill after the mirrored 2 s overfill grace.");
                Signal("s5-client-station-spill-captured");
                fuelClientStationSpillCaptured = true;
                return;
            }

            if (fuelClientStationSpillCaptured && !fuelClientSpillConfirmationSent &&
                HasSignal("s5-host-spill-captured"))
            {
                Signal("s5-client-spill-captured");
                fuelClientSpillConfirmationSent = true;
                return;
            }

            if (fuelClientStationSpillCaptured && HasSignal("s5-close-valve") && game.Shift.ValveOpen)
            {
                SendClientFuelPress();
                SetClientFuelStage(6, now);
            }
            return;
        }

        if (stage == 6)
        {
            game.SetInput(0, new CrewInput());
            if (game.Shift.ValveOpen || game.Shift.FuelTruckTank < 0.999f) return;
            if (!HasSignal("s5-host-station-cleanup-ready")) return;
            Require(game.Shift.NozzleState == NozzlePhase.AtStation,
                "Client mirror did not show the nozzle auto-returned when the valve closed.");
            Require(game.Crew[1].Cart == null, "Client must remain on foot while cleaning the station spill.");
            if (game.Shift.Spills.Count == 0)
            {
                if (!fuelClientCleaning) return;
                Require(!game.Shift.Spilling, "Client removed the spill before the valve was closed.");
                report.AppendLine("PASS Client held the real cleanup action on foot until the spill disappeared from the mirror.");
                signalAfterStep = "s5-client-spill-cleared";
                SetClientFuelStage(7, now);
                return;
            }
            ShiftSpill spill = game.Shift.Spills[0];
            Vector3 spillPoint = new Vector3(spill.X, game.Crew[1].Position.y, spill.Z);
            if (!fuelClientCleaning)
            {
                if (!WalkClientFuelToward(spillPoint, 0.18f)) return;
                game.SetInput(0, new CrewInput { Held = true });
                fuelClientCleaning = true;
                report.AppendLine("Probe: client began holding the cleanup input on foot inside the oil spill zone.");
                return;
            }

            game.SetInput(0, new CrewInput { Held = true });
            return;
        }

        if (stage == 7)
        {
            game.SetInput(0, new CrewInput());
            if (!HasSignal("s5-host-ready-drive")) return;
            Require(game.Shift.NozzleState == NozzlePhase.AtStation && game.Shift.CanDriveCart(FuelCartId),
                "Client mirror: no stow step is needed; the truck must be driveable with the nozzle home.");
            report.AppendLine("PASS Client mirror shows the nozzle home and a driveable truck without any walk-back stow step.");
            SetClientFuelStage(8, now);
            return;
        }

        if (stage == 8)
        {
            if (!HasSignal("s5-host-driving-started") || !HasSignal("s5-host-at-dock")) return;
            if (game.Shift.HoseState == HosePhase.Held && game.Shift.HoseSeat == 1)
            {
                game.SetInput(0, new CrewInput());
                Require(!game.Shift.CanDriveCart(FuelCartId), "Client mirror: a held truck hose must lock the truck.");
                report.AppendLine("PASS Client single tap at the docked truck took the truck's own hose (flight needs fuel, truck has fuel).");
                SetClientFuelStage(9, now);
                return;
            }
            if (!WalkClientFuelToward(game.Carts[FuelCartId].Position, 0.8f)) return;
            if (!fuelClientPressSent)
            {
                Require(game.Crew[1].Cart == null, "Client must be on foot to take the truck hose.");
                SendClientFuelPress();
                fuelClientPressSent = true;
            }
            else game.SetInput(0, new CrewInput());
            return;
        }

        if (stage == 9)
        {
            if (game.Shift.HoseState == HosePhase.OnAircraft && game.Shift.HoseFlightId == fuelFlightId)
            {
                game.SetInput(0, new CrewInput());
                report.AppendLine("PASS Client walked to stand " + fuelFlight.Stand + " and connected the truck hose with one tap through UDP.");
                SetClientFuelStage(10, now);
                return;
            }
            if (fuelClientPressSent)
            {
                game.SetInput(0, new CrewInput());
                return;
            }
            Vector3 dock = AirportGame.Dock(fuelFlight.Stand);
            if (!WalkClientFuelToward(dock, 0.25f)) return;
            Require(game.Crew[1].Cart == null, "Client must be on foot to connect the aircraft hose.");
            Require(Vector3.Distance(game.Carts[FuelCartId].Position, dock) < 1.65f,
                "The host truck is outside the target aircraft hose connection radius.");
            SendClientFuelPress();
            fuelClientPressSent = true;
            return;
        }

        if (stage == 10)
        {
            // M3.3r: no hold — every Remote frame is neutral while fuel flows automatically.
            game.SetInput(0, new CrewInput());
            if (!HasSignal("s5-host-drive-restricted")) return;
            float progress = fuelFlight.Progress[FuelCartId];
            if (!fuelClientAircraftFillCaptured && HasSignal("s5-host-aircraft-fill-captured") && progress >= 0.25f)
            {
                Shot("Evidence/m3-fuel-transfer-client.png", 1920, 1080);
                fuelClientAircraftFillCaptured = true;
                fuelCancelProgress = progress;
                fuelCancelElapsed = game.Shift.MirrorElapsed;
                report.AppendLine("PASS Client captured automatic aircraft fueling with neutral input, then tapped the aircraft to disconnect.");
                SendClientFuelPress();
                signalAfterStep = "s5-client-fill-cancelled-started";
                SetClientFuelStage(11, now);
            }
            return;
        }

        if (stage == 11)
        {
            game.SetInput(0, new CrewInput());
            if (game.Shift.HoseState != HosePhase.OnTruck) return;
            float progress = fuelFlight.Progress[FuelCartId];
            if (game.Shift.MirrorElapsed - fuelCancelElapsed < 0.35f) return;
            if (!fuelCancelStableSampled)
            {
                fuelCancelSampleProgress = progress;
                fuelCancelSampleTank = game.Shift.FuelTruckTank;
                fuelCancelSampleElapsed = game.Shift.MirrorElapsed;
                fuelCancelStableSampled = true;
                return;
            }
            if (game.Shift.MirrorElapsed - fuelCancelSampleElapsed < 0.20f) return;
            Require(Mathf.Abs(progress - fuelCancelSampleProgress) < 0.01f &&
                Mathf.Abs(game.Shift.FuelTruckTank - fuelCancelSampleTank) < 0.01f,
                "Disconnect did not stop fuel transfer.");
            Require(fuelCancelSampleProgress >= fuelCancelProgress - 0.01f &&
                fuelCancelSampleProgress < 0.99f &&
                Mathf.Abs(fuelCancelSampleTank + fuelCancelSampleProgress - 1f) < 0.035f,
                "Disconnect did not preserve the partial fuel progress and tank amount.");
            report.AppendLine("PASS Client mirror retained progress=" + fuelCancelSampleProgress.ToString("0.00") +
                " and tank=" + fuelCancelSampleTank.ToString("0.00") + " after the disconnect tap; hose auto-reeled.");
            signalAfterStep = "s5-client-fill-cancelled";
            SetClientFuelStage(12, now);
            return;
        }

        if (stage == 12)
        {
            if (!HasSignal("s5-resume-fill"))
            {
                game.SetInput(0, new CrewInput());
                return;
            }
            if (fuelFlight.Progress[FuelCartId] >= 1f && game.Shift.FuelTruckTank <= 0.01f)
            {
                game.SetInput(0, new CrewInput());
                Require(fuelFlight.Progress[FuelCartId] == 1f,
                    "Fuel task did not reach exact completion after resuming from the preserved partial fill.");
                SetClientFuelStage(13, now);
                return;
            }
            // Resume = tap the truck (take hose), then tap the aircraft (connect); both from the dock spot.
            if (game.Shift.HoseState == HosePhase.OnAircraft)
            {
                game.SetInput(0, new CrewInput());
                return;
            }
            bool wantTake = game.Shift.HoseState == HosePhase.OnTruck;
            bool wantAttach = game.Shift.HoseState == HosePhase.Held && game.Shift.HoseSeat == 1;
            if (wantTake && !fuelClientHoseTakeSent)
            {
                SendClientFuelPress();
                fuelClientHoseTakeSent = true;
                return;
            }
            if (wantAttach && !fuelClientAttachSent)
            {
                SendClientFuelPress();
                fuelClientAttachSent = true;
                return;
            }
            game.SetInput(0, new CrewInput());
            return;
        }

        if (stage == 13)
        {
            game.SetInput(0, new CrewInput());
            if (!HasSignal("s5-host-deliver-captured")) return;
            Require(fuelFlight.Progress[FuelCartId] == 1f && game.Shift.FuelTruckTank <= 0.01f,
                "Client mirror does not show completed fuel task and consumed truck tank.");
            if (!fuelClientDeliveryCaptured)
            {
                Shot("Evidence/m3-fuel-deliver-client.png", 1920, 1080);
                report.AppendLine("PASS Client captured the completed fuel delivery from its task mirror.");
                Signal("s5-client-deliver-captured");
                fuelClientDeliveryCaptured = true;
                return;
            }
            if (!HasSignal("s5-client-detach-hose")) return;
            if (!game.Shift.HoseSpilling || game.Shift.Spills.Count == 0) return;
            if (!fuelClientDetachSent)
            {
                report.AppendLine("PASS Client mirror shows the stand overfill spill after the 2 s grace; tapping the aircraft while standing on it.");
                SendClientFuelPress();
                fuelClientDetachSent = true;
                SetClientFuelStage(14, now);
            }
            return;
        }

        if (stage == 14)
        {
            if (!fuelClientDockCleaning)
            {
                game.SetInput(0, new CrewInput());
                if (game.Shift.HoseState != HosePhase.OnTruck || game.Shift.Spilling) return;
                Require(game.Shift.HoseSeat == -1 && game.Shift.CanDriveCart(FuelCartId),
                    "Client mirror did not show the disconnected hose reeled onto a driveable truck.");
                report.AppendLine("PASS Client mirror: the aircraft tap disconnected (outranking cleanup) and the overfill stopped.");
                fuelClientDockCleaning = true;
                return;
            }
            if (game.Shift.Spills.Count > 0)
            {
                ShiftSpill spill = game.Shift.Spills[0];
                Vector3 spillPoint = new Vector3(spill.X, game.Crew[1].Position.y, spill.Z);
                if (!WalkClientFuelToward(spillPoint, 0.18f)) return;
                game.SetInput(0, new CrewInput { Held = true });
                return;
            }
            game.SetInput(0, new CrewInput());
            report.AppendLine("PASS Client held cleanup on foot until the stand overfill spill disappeared from the mirror.");
            signalAfterStep = "s5-client-dock-spill-cleared";
            SetClientFuelStage(15, now);
            return;
        }

        if (stage == 15)
        {
            if (!HasSignal("s5-host-close-sent")) return;
            if (SceneManager.GetActiveScene().name != AppState.SceneCabinLobby) return;
            Require(UnityEngine.Object.FindObjectOfType<LobbyApp>() != null,
                "Fuel client returned to CabinLobby without rebuilding LobbyApp.");
            report.AppendLine("PASS Session 5 client returned to CabinLobby after the real host close control.");
            Signal("s5-client-returned");
            Complete(false, "Session 5 (M3.3r) client used single-tap Remote UDP input for every nozzle, valve, hose, movement, disconnect/resume and spill cleanup action; fuel flowed with neutral input and the client mirror matched host fuel state.");
        }
    }

    static void SetClientFuelStage(int value, double now)
    {
        fuelClientPressSent = false;
        SetStage(value, now);
    }

    static void SendClientFuelPress()
    {
        game.SetInput(0, new CrewInput { Pressed = true, Held = true });
        stepOverride = PickupProbeDelta;
    }

    static bool WalkClientFuelToward(Vector3 target, float radius)
    {
        Crew client = game.Crew[1];
        Vector3 delta = target - client.Position;
        delta.y = 0f;
        if (delta.magnitude <= radius)
        {
            game.SetInput(0, new CrewInput());
            return true;
        }
        Vector2 move = new Vector2(delta.x, delta.z).normalized;
        game.SetInput(0, new CrewInput { Move = move });
        return false;
    }

    static string FuelDiagnostics()
    {
        if (game == null || game.Shift == null) return ",fuel=none";
        Cart truck = game.Carts.Count > FuelCartId ? game.Carts[FuelCartId] : null;
        Flight flight = FindMirrorFlight(fuelFlightId);
        return ",nozzle=" + game.Shift.NozzleState + ",nozzleSeat=" + game.Shift.NozzleSeat +
            ",hose=" + game.Shift.HoseState + ",hoseSeat=" + game.Shift.HoseSeat + ",hoseFlight=" + game.Shift.HoseFlightId +
            ",overfill=" + game.Shift.StationOverfill.ToString("0.00") + "/" + game.Shift.HoseOverfill.ToString("0.00") +
            ",valve=" + game.Shift.ValveOpen + ",tank=" + game.Shift.FuelTruckTank.ToString("R") +
            ",spills=" + game.Shift.Spills.Count + ",fuelProgress=" +
            (flight == null ? "none" : flight.Progress[FuelCartId].ToString("R")) +
            ",truck=" + (truck == null ? "none" : truck.Position.ToString()) +
            ",truckOwner=" + (truck == null || truck.Owner == null ? "none" : truck.Owner.Index.ToString()) +
            ",driveWaypoint=" + fuelDriveWaypoint;
    }
}
