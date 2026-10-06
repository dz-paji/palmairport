using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using IslandAirport;

public static partial class NetPlaytest
{
    static Flight boardingFlight;
    static string boardingFlightId;
    static Vector3 boardingBlockedPosition;
    static Vector3 boardingMoveStart;
    static int boardingWaitingAtClose;
    static float boardingProgressAtClose;
    static string boardingWaitingIds;

    public static void RunHostBoarding() { StartRun("host-boarding"); }
    public static void RunClientBoarding() { StartRun("client-boarding"); }

    static void AdvanceHostBoarding(double now, float dt)
    {
        if (stage == 0)
        {
            Require(SceneManager.GetActiveScene().name == AppState.SceneCabinLobby, "Boarding host did not start in CabinLobby.");
            Require(state.Room.SocketFactory == null && state.CanPlayWithOthers, "Boarding host requires real UDP and eligible FakeAuth.");
            Require(state.Room.Host("NetPlay Boarding", "NetplayHost6", FakeAuthToken()), "Boarding Room.Host failed.");
            Signal("s6-host-ready");
            report.AppendLine("PASS Session 6 host bound a real UDP socket; fixtures will set spatial positions and meal/fuel prerequisites only.");
            SetStage(1, now);
            return;
        }
        if (stage == 1)
        {
            if (!state.Room.Seats[1].Occupied) return;
            LobbyApp lobby = UnityEngine.Object.FindObjectOfType<LobbyApp>();
            Require(lobby != null, "Boarding LobbyApp unavailable.");
            lobby.BeginPracticeShift();
            SetStage(2, now);
            return;
        }
        if (stage == 2)
        {
            if (!game || !game.Started) return;
            Require(game.Sandbox && !game.Remote && game.Sim.Endless, "Boarding host must use endless task practice.");
            boardingFlight = FirstServicingFlight();
            if (boardingFlight == null || !game.StandReady(boardingFlight.Stand)) return;
            boardingFlightId = boardingFlight.Id;
            Crew client = game.Crew[1];
            client.Selected = game.Sim.Flights.IndexOf(boardingFlight);
            client.Position = AirportGame.Station(ServiceKind.Boarding);
            client.Visual.position = client.Position;
            game.Crew[0].Selected = client.Selected;
            Cart cart = game.Carts[(int)ServiceKind.Meals];
            cart.Position = Level1Map.FromDesign(210, 133);
            cart.Visual.position = cart.Position;
            game.Crew[0].Position = cart.Position;
            game.Crew[0].Visual.position = cart.Position;
            Require(Level1Map.InDrivable(cart.Position, .35f), "Boarding vehicle fixture must lie on a real service road.");
            game.SetInput(0, new CrewInput { Pressed = true });
            stepOverride = PickupProbeDelta;
            File.WriteAllText(Path.Combine(controlDirectory, "s6-flight-id"), boardingFlightId);
            signalAfterStep = "s6-fixtures-ready";
            report.AppendLine("Fixture: scheduler supplied " + boardingFlightId + "; client stands at gate, host meal cart parked north of its pavement crossing. Prerequisites remain incomplete.");
            SetStage(3, now);
            return;
        }
        if (stage == 3)
        {
            Require(game.Crew[0].Cart == game.Carts[0] && game.Carts[0].Owner == game.Crew[0], "Host real local Pressed input did not mount the crossing vehicle.");
            if (!HasSignal("s6-client-prereq-pressed") || game.Toast.IndexOf("先完成餐食与燃油", StringComparison.Ordinal) < 0) return;
            Require(!game.Shift.IsBoarding(boardingFlightId) && boardingFlight.Progress[3] == 0f, "Incomplete prerequisites allowed boarding.");
            Require(game.Sim.TryAdvance(boardingFlight, ServiceKind.Meals, 1f) && game.Sim.TryAdvance(boardingFlight, ServiceKind.Fuel, 1f), "Could not apply meal/fuel prerequisite fixture.");
            report.AppendLine("PASS Client gate intent arrived over UDP and was refused with meal/fuel attribution. Fixture now completes those two tasks; boarding remains untouched.");
            signalAfterStep = "s6-prerequisites-ready";
            SetStage(4, now);
            return;
        }
        if (stage == 4)
        {
            if (!game.Shift.IsBoarding(boardingFlightId)) return;
            // Drive toward the crossing only as an actual released passenger approaches the spine.
            for (int i = 0; i < game.Shift.Passengers.Count; i++)
            {
                ShiftPassenger passenger = game.Shift.Passengers[i];
                if (passenger.FlightId != boardingFlightId || !passenger.Released) continue;
                Vector3 point = game.PassengerPosition(passenger);
                if (Mathf.Abs(point.x - game.Crew[0].Position.x) > .85f) continue;
                boardingMoveStart = game.Crew[0].Position;
                game.SetInput(0, new CrewInput { Move = Vector2.down });
                SetStage(5, now);
                report.AppendLine("Probe: host mounted cart now drives south toward a released passenger crossing the service spine.");
                return;
            }
            return;
        }
        if (stage == 5)
        {
            if (!game.Crew[0].BlockedByTraffic)
            {
                boardingMoveStart = game.Crew[0].Position;
                game.SetInput(0, new CrewInput { Move = Vector2.down });
                return;
            }
            boardingBlockedPosition = game.Crew[0].Position;
            Require(Vector3.Distance(boardingMoveStart, boardingBlockedPosition) < .0001f, "Host blocked Move still displaced the vehicle.");
            Require(BoardingPassengerCount(false) > 0, "Blocking probe released the entire queue before closing could be tested.");
            bool crossing = false;
            for (int i = 0; i < game.Shift.Passengers.Count; i++)
                if (game.PassengerBlocksSegment(boardingBlockedPosition, boardingBlockedPosition + Vector3.back * .36f, game.Shift.Passengers[i])) crossing = true;
            Require(crossing, "Host blocked flag lacks a released passenger on its attempted movement segment.");
            WriteBoardingExpected("blocked");
            FreezeBoarding(dt);
            Shot("Evidence/m3-boarding-blocked-host.png", 1920, 1080);
            Signal("s6-blocked-captured");
            report.AppendLine("PASS Host Move refused vehicle movement at a released passenger; same-state snapshot frozen at " + boardingBlockedPosition + ".");
            SetStage(6, now);
            return;
        }
        if (stage == 6)
        {
            FreezeBoarding(dt);
            if (!HasSignal("s6-client-blocked-captured")) return;
            Signal("s6-close-gate");
            SetStage(7, now);
            return;
        }
        if (stage == 7)
        {
            game.SetInput(0, new CrewInput());
            if (game.Shift.IsBoarding(boardingFlightId)) return;
            boardingWaitingAtClose = BoardingPassengerCount(false);
            boardingWaitingIds = BoardingWaitingIds();
            boardingProgressAtClose = boardingFlight.Progress[3];
            Require(boardingWaitingAtClose > 0 && BoardingPassengerCount(true) > 0, "Close must retain both waiting and already released passengers.");
            report.AppendLine("PASS Client UDP close stopped new release with " + boardingWaitingAtClose + " waiting and " + BoardingPassengerCount(true) + " walkers already underway.");
            SetStage(8, now);
            return;
        }
        if (stage == 8)
        {
            Require(!game.Shift.IsBoarding(boardingFlightId), "Closed gate unexpectedly reopened.");
            Require(BoardingPassengerCount(false) == boardingWaitingAtClose && BoardingWaitingIds() == boardingWaitingIds, "Closed gate released or replaced waiting passengers.");
            Require(boardingFlight.Progress[3] >= boardingProgressAtClose, "Close rolled back boarding progress.");
            if (BoardingPassengerCount(true) != 0) return;
            Require(boardingFlight.Progress[3] > boardingProgressAtClose && boardingFlight.Progress[3] < 1f, "Launched passengers did not continue boarding after close.");
            boardingProgressAtClose = boardingFlight.Progress[3];
            WriteBoardingExpected("closed");
            FreezeBoarding(dt);
            Shot("Evidence/m3-boarding-closed-host.png", 1920, 1080);
            Signal("s6-closed-captured");
            report.AppendLine("PASS All launched passengers reached the aircraft while closed; partial progress=" + boardingProgressAtClose + ", same waiting IDs retained.");
            SetStage(9, now);
            return;
        }
        if (stage == 9)
        {
            FreezeBoarding(dt);
            if (!HasSignal("s6-client-closed-captured")) return;
            Signal("s6-reopen-gate");
            SetStage(10, now);
            return;
        }
        if (stage == 10)
        {
            if (!game.Shift.IsBoarding(boardingFlightId)) return;
            Require(boardingFlight.Progress[3] >= boardingProgressAtClose, "Reopen lost partial progress.");
            report.AppendLine("PASS Client UDP reopen resumed the remaining queue from preserved progress.");
            SetStage(11, now);
            return;
        }
        if (stage == 11)
        {
            if (boardingFlight.Progress[3] < 1f) return;
            Require(BoardingPassengerCount(true) == 0 && BoardingPassengerCount(false) == 0, "Completed flight retained passenger manifest.");
            Require(!game.Shift.IsBoarding(boardingFlightId), "Completed flight retained open gate.");
            RequireCompletedBoardingBotChoosesBaggage();
            WriteBoardingExpected("complete");
            FreezeBoarding(dt);
            Shot("Evidence/m3-boarding-complete-host.png", 1920, 1080);
            Signal("s6-complete-captured");
            report.AppendLine("PASS Host boarding completed exactly once at 1.0 with no passengers or open gate left.");
            SetStage(12, now);
            return;
        }
        if (stage == 12)
        {
            FreezeBoarding(dt);
            if (!HasSignal("s6-client-complete-captured")) return;
            state.Room.CloseRoom();
            Signal("s6-host-close-sent");
            SetStage(13, now);
            return;
        }
        if (stage == 13)
        {
            if (!HasSignal("s6-client-returned")) return;
            Complete(false, "Session 6 proved real client UDP gate prerequisite refusal/open/close/reopen, host vehicle passenger blocking, closed-gate continuation and preserved progress, with exact frozen snapshot pairs. Fixtures only placed actors and completed meal/fuel prerequisites.");
        }
    }

    static void AdvanceClientBoarding(double now, float dt)
    {
        if (stage == 0)
        {
            if (!HasSignal("s6-host-ready")) return;
            Require(state.Room.SocketFactory == null && state.CanPlayWithOthers, "Boarding client requires real UDP and eligible FakeAuth.");
            state.Room.Join(joinAddress, "NetplayClient6", FakeAuthToken());
            report.AppendLine("PASS Session 6 client joined through a real UDP socket.");
            SetStage(1, now);
            return;
        }
        if (stage == 1)
        {
            if (!game || !game.Started || !game.Remote || !HasSignal("s6-fixtures-ready")) return;
            boardingFlightId = File.ReadAllText(Path.Combine(controlDirectory, "s6-flight-id"));
            boardingFlight = FindMirrorFlight(boardingFlightId);
            Flight selected = game.SelectedFlight(game.Crew[1]);
            if (boardingFlight == null || selected == null || selected.Id != boardingFlightId || Vector3.Distance(game.Crew[1].Position, AirportGame.Station(ServiceKind.Boarding)) > .1f) return;
            int selectedBefore = game.Crew[1].Selected;
            Require(game.SelectedFlight(game.Crew[1]).Id == boardingFlightId && game.Crew[1].Selected == selectedBefore,
                "Client mirrored selection must resolve without mutating its selected index.");
            Crew invalidSelection = new Crew { Selected = -1 };
            Require(game.SelectedFlight(invalidSelection) != null && invalidSelection.Selected == -1,
                "Client read-only fallback must resolve a servicing flight without rewriting selection.");
            GameObject route = GameObject.Find("Selected flight passenger route");
            Require(route != null && route.activeInHierarchy && route.GetComponent<LineRenderer>().positionCount > 1,
                "Client selected flight did not render a visible passenger route before opening.");
            FieldInfo visualsField = typeof(AirportGame).GetField("passengerVisuals", BindingFlags.Instance | BindingFlags.NonPublic);
            var visuals = visualsField.GetValue(game) as System.Collections.IDictionary;
            Require(visuals != null && visuals.Count >= ShiftSim.PassengersPerGate,
                "Client did not render the waiting queue before opening the selected gate.");
            report.AppendLine("PASS Client mirrored selection and fallback are read-only; selected route and waiting queue render before any gate action.");
            SendBoardingClientPress();
            signalAfterStep = "s6-client-prereq-pressed";
            SetStage(2, now);
            return;
        }
        if (stage == 2)
        {
            game.SetInput(0, new CrewInput());
            if (!HasSignal("s6-prerequisites-ready")) return;
            boardingFlight = FindMirrorFlight(boardingFlightId);
            if (boardingFlight == null || boardingFlight.Progress[0] < 1f || boardingFlight.Progress[2] < 1f) return;
            Require(boardingFlight.Progress[3] == 0f && !game.Shift.IsBoarding(boardingFlightId), "Prerequisite fixture changed boarding state.");
            SendBoardingClientPress();
            report.AppendLine("Probe: client Pressed input opens selected flight gate over Remote UDP.");
            SetStage(3, now);
            return;
        }
        if (stage == 3)
        {
            game.SetInput(0, new CrewInput());
            if (!HasSignal("s6-blocked-captured") || !MatchesBoardingExpected("blocked")) return;
            Require(game.Crew[0].BlockedByTraffic && game.Crew[0].Cart == game.Carts[0], "Client snapshot did not mirror host's blocked mounted cart.");
            Shot("Evidence/m3-boarding-blocked-client.png", 1920, 1080);
            Signal("s6-client-blocked-captured");
            report.AppendLine("PASS Client mirrored exactly the frozen host blocked cart, released/waiting passengers, open gate, and task progress.");
            SetStage(4, now);
            return;
        }
        if (stage == 4)
        {
            game.SetInput(0, new CrewInput());
            if (!HasSignal("s6-close-gate")) return;
            SendBoardingClientPress();
            report.AppendLine("Probe: client Pressed input closes the same flight gate over Remote UDP.");
            SetStage(5, now);
            return;
        }
        if (stage == 5)
        {
            game.SetInput(0, new CrewInput());
            if (!HasSignal("s6-closed-captured") || !MatchesBoardingExpected("closed")) return;
            Require(!game.Shift.IsBoarding(boardingFlightId) && BoardingPassengerCount(false) > 0 && BoardingPassengerCount(true) == 0, "Client closed snapshot lost waiting queue or retained completed walkers.");
            boardingFlight = FindMirrorFlight(boardingFlightId);
            Require(boardingFlight.Progress[3] > 0f && boardingFlight.Progress[3] < 1f, "Client close did not preserve partial boarding.");
            Shot("Evidence/m3-boarding-closed-client.png", 1920, 1080);
            Signal("s6-client-closed-captured");
            report.AppendLine("PASS Client mirrored the frozen closed gate after launched passengers boarded; same stable waiting IDs and partial task progress.");
            SetStage(6, now);
            return;
        }
        if (stage == 6)
        {
            game.SetInput(0, new CrewInput());
            if (!HasSignal("s6-reopen-gate")) return;
            SendBoardingClientPress();
            report.AppendLine("Probe: client Pressed input reopens the gate to resume the remaining queue over UDP.");
            SetStage(7, now);
            return;
        }
        if (stage == 7)
        {
            game.SetInput(0, new CrewInput());
            if (!HasSignal("s6-complete-captured") || !MatchesBoardingExpected("complete")) return;
            boardingFlight = FindMirrorFlight(boardingFlightId);
            Require(boardingFlight.Progress[3] == 1f && !game.Shift.IsBoarding(boardingFlightId), "Client final task mirror is incomplete.");
            Shot("Evidence/m3-boarding-complete-client.png", 1920, 1080);
            Signal("s6-client-complete-captured");
            report.AppendLine("PASS Client mirrored exact boarding completion after reopening, with no duplicated passenger IDs or open gate.");
            SetStage(8, now);
            return;
        }
        if (stage == 8)
        {
            if (!HasSignal("s6-host-close-sent") || SceneManager.GetActiveScene().name != AppState.SceneCabinLobby) return;
            Require(UnityEngine.Object.FindObjectOfType<LobbyApp>() != null, "Boarding client returned without LobbyApp.");
            Signal("s6-client-returned");
            Complete(false, "Session 6 client sent every gate action through Remote UDP and matched host passenger/task/blocked vehicle snapshots at blocked, closed, and complete capture points; returned to lobby after host close.");
        }
    }

    static void RequireCompletedBoardingBotChoosesBaggage()
    {
        Cart baggage = game.Carts[(int)ServiceKind.Baggage];
        Require(boardingFlight.Progress[(int)ServiceKind.Baggage] < 1f && baggage.Owner == null,
            "Boarding bot regression requires an unfinished baggage task and available baggage vehicle.");
        // Probe the same bot routing method with a detached crew shell; no live input,
        // ownership, flight state, or actual actor position is changed by this check.
        Crew probe = new Crew { Index = 0, Position = baggage.Position };
        MethodInfo method = typeof(AirportGame).GetMethod("BotGeneralInput", BindingFlags.Instance | BindingFlags.NonPublic);
        Require(method != null, "Boarding bot regression could not find gameplay bot routing.");
        // Reflection does not supply optional parameters. -1 preserves the normal
        // unfinished-task fallback, so this still exercises the completed-gate guard.
        CrewInput next = (CrewInput)method.Invoke(game, new object[] { probe, .1f, boardingFlight, -1 });
        Require(next.Pressed && next.Move.sqrMagnitude < .0001f,
            "Bot revisited the completed boarding gate instead of choosing the remaining baggage vehicle.");
        report.AppendLine("PASS Gameplay bot routing chooses the available baggage vehicle after boarding completes; completed gates do not starve remaining baggage work.");
    }

    static void SendBoardingClientPress()
    {
        game.SetInput(0, new CrewInput { Pressed = true });
        stepOverride = PickupProbeDelta;
    }

    static int BoardingPassengerCount(bool released)
    {
        int count = 0;
        for (int i = 0; i < game.Shift.Passengers.Count; i++)
            if (game.Shift.Passengers[i].FlightId == boardingFlightId && game.Shift.Passengers[i].Released == released) count++;
        return count;
    }

    static string BoardingWaitingIds()
    {
        string ids = string.Empty;
        for (int i = 0; i < game.Shift.Passengers.Count; i++)
        {
            ShiftPassenger passenger = game.Shift.Passengers[i];
            if (passenger.FlightId == boardingFlightId && !passenger.Released)
                ids += passenger.Seq + ":" + passenger.Delay.ToString("R", CultureInfo.InvariantCulture) + ";";
        }
        return ids;
    }

    // Freeze domain time only after the real gameplay action has happened. Keep emitting
    // live UDP snapshots while the second editor samples the identical state and captures.
    static void FreezeBoarding(float dt)
    {
        suppressGameStep = true;
        FieldInfo field = typeof(AirportGame).GetField("netSession", BindingFlags.Instance | BindingFlags.NonPublic);
        Require(field != null, "Boarding freeze cannot locate NetSession.");
        NetSession session = field.GetValue(game) as NetSession;
        Require(session != null, "Boarding freeze requires the existing live host NetSession.");
        session.StepHost(dt);
    }

    static void WriteBoardingExpected(string point)
    {
        using (StreamWriter writer = new StreamWriter(Path.Combine(controlDirectory, "s6-expected-" + point)))
        {
            writer.WriteLine(game.Crew[0].Position.x.ToString("R", CultureInfo.InvariantCulture));
            writer.WriteLine(game.Crew[0].Position.z.ToString("R", CultureInfo.InvariantCulture));
            writer.WriteLine(game.Crew[0].BlockedByTraffic);
            writer.WriteLine(boardingFlight.Progress[3].ToString("R", CultureInfo.InvariantCulture));
            writer.WriteLine(game.Shift.IsBoarding(boardingFlightId));
            writer.WriteLine(game.Shift.Passengers.Count);
            for (int i = 0; i < game.Shift.Passengers.Count; i++)
            {
                ShiftPassenger passenger = game.Shift.Passengers[i];
                writer.WriteLine(passenger.FlightId + "|" + passenger.Seq + "|" + passenger.Waypoint + "|" + passenger.Released + "|" +
                    passenger.Progress01.ToString("R", CultureInfo.InvariantCulture) + "|" + passenger.Delay.ToString("R", CultureInfo.InvariantCulture));
            }
        }
    }

    static bool MatchesBoardingExpected(string point)
    {
        string[] rows = File.ReadAllLines(Path.Combine(controlDirectory, "s6-expected-" + point));
        Flight flight = FindMirrorFlight(boardingFlightId);
        if (flight == null || Mathf.Abs(game.Crew[0].Position.x - float.Parse(rows[0], CultureInfo.InvariantCulture)) > .015f ||
            Mathf.Abs(game.Crew[0].Position.z - float.Parse(rows[1], CultureInfo.InvariantCulture)) > .015f ||
            game.Crew[0].BlockedByTraffic != bool.Parse(rows[2]) || Mathf.Abs(flight.Progress[3] - float.Parse(rows[3], CultureInfo.InvariantCulture)) > .001f ||
            game.Shift.IsBoarding(boardingFlightId) != bool.Parse(rows[4]) || game.Shift.Passengers.Count != int.Parse(rows[5])) return false;
        for (int i = 0; i < game.Shift.Passengers.Count; i++)
        {
            string[] expected = rows[i + 6].Split('|');
            ShiftPassenger passenger = game.Shift.Passengers[i];
            if (passenger.FlightId != expected[0] || passenger.Seq != int.Parse(expected[1]) || passenger.Waypoint != int.Parse(expected[2]) ||
                passenger.Released != bool.Parse(expected[3]) || Mathf.Abs(passenger.Progress01 - float.Parse(expected[4], CultureInfo.InvariantCulture)) > .001f ||
                Mathf.Abs(passenger.Delay - float.Parse(expected[5], CultureInfo.InvariantCulture)) > .001f) return false;
        }
        return true;
    }

    static string BoardingDiagnostics()
    {
        if (game == null || game.Shift == null) return ",boarding=none";
        Flight flight = FindMirrorFlight(boardingFlightId);
        return ",boardingFlight=" + boardingFlightId + ",gate=" + game.Shift.IsBoarding(boardingFlightId) +
            ",waiting=" + BoardingPassengerCount(false) + ",walking=" + BoardingPassengerCount(true) +
            ",boardingProgress=" + (flight == null ? "none" : flight.Progress[3].ToString("0.000")) +
            ",blocked=" + (game.Crew.Count == 0 ? "none" : game.Crew[0].BlockedByTraffic.ToString());
    }
}
