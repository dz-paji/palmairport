using System;
using System.Collections.Generic;
using IslandAirport;

/// <summary>
/// Dependency-free console coverage for AirportSimulation. The test is kept
/// outside the Unity project runtime so it can run with mcs and mono alone.
/// </summary>
public static class SimulationTests
{
    private static int _passed;
    private static int _failed;
    private static int _assertions;

    public static void Main()
    {
        Run("staggered arrivals and stand assignment", TestStaggeredArrivals);
        Run("three stands queue and reuse", TestThreeStandsQueueAndReuse);
        Run("service prerequisites", TestServicePrerequisites);
        Run("invalid input is rejected", TestInvalidInput);
        Run("large and small ticks agree at deadlines", TestLargeAndSmallTicksAgree);
        Run("deadline is processed at the exact boundary", TestExactDeadline);
        Run("ending the shift freezes all state", TestEndShiftFreezesState);
        Run("partial contribution is retained and score is idempotent", TestPartialContributionAndNoRepeatScore);
        Run("all tasks complete and the flight departs", TestCompleteFlightDeparts);
        Run("stars use the 1, 2, and 4 flight thresholds", TestStarThresholds);
        Run("endless shift with empty flight table never settles", TestEndlessEmptyFlightTable);

        Console.WriteLine("Simulation tests: {0} passed, {1} failed, {2} assertions.",
            _passed, _failed, _assertions);

        if (_failed != 0)
        {
            Environment.Exit(1);
        }
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine("PASS  " + name);
        }
        catch (Exception exception)
        {
            _failed++;
            Console.WriteLine("FAIL  " + name + ": " + exception.Message);
            Console.WriteLine(exception.ToString());
        }
    }

    private static void Assert(bool condition, string message)
    {
        _assertions++;
        if (!condition)
        {
            throw new Exception(message);
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        _assertions++;
        if (!object.Equals(expected, actual))
        {
            throw new Exception(message + " expected=" + expected + " actual=" + actual);
        }
    }

    private static void AssertNear(float expected, float actual, float tolerance, string message)
    {
        _assertions++;
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new Exception(message + " expected=" + expected + " actual=" + actual);
        }
    }

    private static void AssertStatus(Flight flight, FlightStatus expected, string message)
    {
        AssertEqual(expected, flight.Status, message + " status");
    }

    private static Flight[] CreateFlights(params float[] deadlines)
    {
        Flight[] flights = new Flight[deadlines.Length];
        for (int i = 0; i < deadlines.Length; i++)
        {
            flights[i] = new Flight("F" + (i + 1), 0f, deadlines[i]);
        }

        return flights;
    }

    private static void CompleteFlight(AirportSimulation simulation, Flight flight)
    {
        Assert(simulation.ReturnArrivalBags(flight), flight.Id + " should accept arrival baggage");
        Assert(simulation.TryAdvance(flight, ServiceKind.Meals, 1f), flight.Id + " meals should complete");
        Assert(simulation.TryAdvance(flight, ServiceKind.Fuel, 1f), flight.Id + " fuel should complete");
        Assert(simulation.TryAdvance(flight, ServiceKind.Baggage, 1f), flight.Id + " baggage should complete");
        Assert(simulation.TryAdvance(flight, ServiceKind.Boarding, 1f), flight.Id + " boarding should complete");
    }

    private static void TestStaggeredArrivals()
    {
        AirportSimulation defaults = new AirportSimulation();
        AssertEqual(5, defaults.Flights.Count, "default schedule should contain five flights");
        AssertNear(0f, defaults.Flights[0].ArrivalTime, 0.0001f, "F1 arrival");
        AssertNear(35f, defaults.Flights[1].ArrivalTime, 0.0001f, "F2 arrival");
        AssertNear(85f, defaults.Flights[2].ArrivalTime, 0.0001f, "F3 arrival");
        AssertNear(140f, defaults.Flights[3].ArrivalTime, 0.0001f, "F4 arrival");
        AssertNear(195f, defaults.Flights[4].ArrivalTime, 0.0001f, "F5 arrival");
        AssertStatus(defaults.Flights[0], FlightStatus.Servicing, "first default flight should land immediately");
        AssertStatus(defaults.Flights[1], FlightStatus.Scheduled, "second default flight should wait for its arrival");

        Flight first = new Flight("A", 0f, 75f);
        Flight second = new Flight("B", 35f, 110f);
        Flight third = new Flight("C", 85f, 160f);
        AirportSimulation simulation = new AirportSimulation(new[] { first, second, third });

        AssertEqual(0, first.Stand, "first arrival should use stand 0");
        AssertEqual(-1, second.Stand, "future arrival should have no stand");
        AssertEqual(-1, third.Stand, "future arrival should have no stand");
        simulation.Tick(35f);
        AssertStatus(second, FlightStatus.Servicing, "second flight should land at its arrival time");
        AssertEqual(1, second.Stand, "second arrival should use the other stand");
        AssertStatus(third, FlightStatus.Scheduled, "third flight should still be queued");
        simulation.Tick(40f);
        AssertStatus(first, FlightStatus.Missed, "first flight should miss at deadline 75");
        AssertEqual(-1, first.Stand, "missed flight should release its stand");
        AssertStatus(second, FlightStatus.Servicing, "second flight should remain in service");
        AssertStatus(third, FlightStatus.Scheduled, "third flight should remain queued before arrival");
        simulation.Tick(10f);
        AssertStatus(third, FlightStatus.Servicing, "third flight should land at arrival 85");
        AssertEqual(0, third.Stand, "third flight should take the released stand");
        AssertEqual(1, simulation.MissedCount, "only the first flight should have missed");
    }

    private static void TestThreeStandsQueueAndReuse()
    {
        Flight first = new Flight("A", 0f, 100f);
        Flight second = new Flight("B", 0f, 100f);
        Flight third = new Flight("C", 0f, 100f);
        Flight fourth = new Flight("D", 0f, 100f);
        AirportSimulation simulation = new AirportSimulation(new[] { first, second, third, fourth });

        AssertEqual(first, simulation.ActiveAtStand(0), "stand 0 should contain the first flight");
        AssertEqual(second, simulation.ActiveAtStand(1), "stand 1 should contain the second flight");
        AssertEqual(third, simulation.ActiveAtStand(2), "stand 2 should contain the third flight");
        AssertStatus(fourth, FlightStatus.Scheduled, "fourth simultaneous arrival should wait");
        AssertEqual(-1, fourth.Stand, "queued flight should not reserve a stand");
        AssertEqual(null, simulation.ActiveAtStand(-1), "negative stand should be invalid");
        AssertEqual(null, simulation.ActiveAtStand(3), "stand after the three stand range should be invalid");

        CompleteFlight(simulation, first);
        AssertStatus(first, FlightStatus.Departed, "first flight should depart");
        AssertStatus(fourth, FlightStatus.Servicing, "queued flight should take the freed stand");
        AssertEqual(0, fourth.Stand, "queued flight should take stand 0");
        AssertEqual(fourth, simulation.ActiveAtStand(0), "stand 0 should now contain the queued flight");

        CompleteFlight(simulation, second);
        CompleteFlight(simulation, third);
        CompleteFlight(simulation, fourth);
        AssertEqual(4, simulation.DepartedCount, "all four flights should depart");
        AssertEqual(0, simulation.MissedCount, "no flight should miss");
        AssertEqual(null, simulation.ActiveAtStand(0), "stand 0 should be free after departure");
        AssertEqual(null, simulation.ActiveAtStand(1), "stand 1 should be free after departure");
        AssertEqual(null, simulation.ActiveAtStand(2), "stand 2 should be free after departure");
    }

    private static void TestServicePrerequisites()
    {
        Flight flight = new Flight("P", 0f, 100f);
        AirportSimulation simulation = new AirportSimulation(new[] { flight });

        Assert(!simulation.TryAdvance(flight, ServiceKind.Baggage, 1f),
            "baggage should require returned arrival bags");
        Assert(!simulation.TryAdvance(flight, ServiceKind.Boarding, 1f),
            "boarding should require meals and fuel");
        Assert(simulation.TryAdvance(flight, ServiceKind.Meals, 0.5f), "half of meals should be accepted");
        AssertNear(0.5f, flight.Progress[(int)ServiceKind.Meals], 0.0001f, "meal contribution should be retained");
        Assert(!simulation.TryAdvance(flight, ServiceKind.Boarding, 1f),
            "boarding should remain locked until fuel is complete");
        Assert(simulation.TryAdvance(flight, ServiceKind.Meals, 0.5f), "remaining meals should be accepted");
        Assert(simulation.TryAdvance(flight, ServiceKind.Fuel, 1f), "fuel should complete");
        Assert(!simulation.TryAdvance(flight, ServiceKind.Baggage, 1f),
            "baggage should still be locked before arrival bags are returned");
        Assert(simulation.ReturnArrivalBags(flight), "arrival bags should be returnable at the stand");
        Assert(!simulation.ReturnArrivalBags(flight), "arrival bags should not be returned twice");
        Assert(simulation.TryAdvance(flight, ServiceKind.Baggage, 1f), "baggage should complete after return");
        Assert(simulation.TryAdvance(flight, ServiceKind.Boarding, 1f), "boarding should complete after meals and fuel");
        AssertStatus(flight, FlightStatus.Departed, "all four services should trigger departure");
        AssertEqual(4, simulation.CompletedTaskCount, "each service should count once");
    }

    private static void TestInvalidInput()
    {
        Flight flight = new Flight("I", 0f, 100f, 99);
        Flight outsider = new Flight("outside", 0f, 100f);
        AssertEqual(-1, flight.Stand, "invalid constructor stand should normalize to -1");
        AirportSimulation simulation = new AirportSimulation(new[] { flight });

        AssertEqual(0, flight.Stand, "valid arriving flight should then be assigned to stand 0");
        Assert(!simulation.ReturnArrivalBags(null), "null flight should be rejected by bag return");
        Assert(!simulation.ReturnArrivalBags(outsider), "foreign flight should be rejected by bag return");
        Assert(!simulation.TryAdvance(null, ServiceKind.Meals, 1f), "null flight should be rejected by service advance");
        Assert(!simulation.TryAdvance(outsider, ServiceKind.Meals, 1f), "foreign flight should be rejected by service advance");
        Assert(!simulation.TryAdvance(flight, (ServiceKind)99, 1f), "unknown service should be rejected");
        Assert(!simulation.TryAdvance(flight, ServiceKind.Meals, 0f), "zero amount should be rejected");
        Assert(!simulation.TryAdvance(flight, ServiceKind.Meals, -1f), "negative amount should be rejected");
        Assert(!simulation.TryAdvance(flight, ServiceKind.Meals, float.NaN), "NaN amount should be rejected");
        Assert(!simulation.TryAdvance(flight, ServiceKind.Meals, float.PositiveInfinity), "infinite amount should be rejected");
        Assert(!simulation.TryAdvance(flight, ServiceKind.Meals, float.NegativeInfinity), "negative infinite amount should be rejected");

        bool progressException = false;
        try
        {
            flight.Progress = new float[3];
        }
        catch (ArgumentException)
        {
            progressException = true;
        }
        Assert(progressException, "progress arrays with the wrong size should be rejected");

        float elapsed = simulation.Elapsed;
        simulation.Tick(0f);
        simulation.Tick(-1f);
        simulation.Tick(float.NaN);
        simulation.Tick(float.PositiveInfinity);
        AssertNear(elapsed, simulation.Elapsed, 0.0001f, "invalid ticks should not advance time");
    }

    private static Flight[] CreateDeadlineScenario()
    {
        return new[]
        {
            new Flight("D1", 0f, 10f),
            new Flight("D2", 5f, 15f),
            new Flight("D3", 12f, 25f)
        };
    }

    private static void AssertSameFlightState(Flight expected, Flight actual)
    {
        AssertEqual(expected.Id, actual.Id, "flight ids should match");
        AssertEqual(expected.Status, actual.Status, expected.Id + " status should match");
        AssertEqual(expected.Stand, actual.Stand, expected.Id + " stand should match");
        AssertEqual(expected.ArrivalBagsReturned, actual.ArrivalBagsReturned,
            expected.Id + " arrival baggage state should match");
        for (int i = 0; i < expected.Progress.Length; i++)
        {
            AssertNear(expected.Progress[i], actual.Progress[i], 0.0001f,
                expected.Id + " progress " + i + " should match");
        }
    }

    private static void TestLargeAndSmallTicksAgree()
    {
        Flight[] largeFlights = CreateDeadlineScenario();
        Flight[] smallFlights = CreateDeadlineScenario();
        AirportSimulation large = new AirportSimulation(largeFlights);
        AirportSimulation small = new AirportSimulation(smallFlights);

        large.Tick(30f);
        for (int i = 0; i < 30; i++)
        {
            small.Tick(1f);
        }

        AssertNear(30f, large.Elapsed, 0.0001f, "large tick should reach target time");
        AssertNear(30f, small.Elapsed, 0.0001f, "small ticks should reach the same target time");
        AssertEqual(large.MissedCount, small.MissedCount, "miss counts should be identical");
        AssertEqual(large.DepartedCount, small.DepartedCount, "departure counts should be identical");
        AssertEqual(large.CompletedTaskCount, small.CompletedTaskCount, "task counts should be identical");
        AssertEqual(large.Score, small.Score, "scores should be identical");
        AssertEqual(large.LastEvent, small.LastEvent, "last event should be identical");
        for (int i = 0; i < large.Flights.Count; i++)
        {
            AssertSameFlightState(large.Flights[i], small.Flights[i]);
        }
        AssertStatus(largeFlights[0], FlightStatus.Missed, "first deadline should be observed by a large tick");
        AssertStatus(largeFlights[1], FlightStatus.Missed, "second deadline should be observed by a large tick");
        AssertStatus(largeFlights[2], FlightStatus.Missed, "third deadline should be observed by a large tick");
    }

    private static void TestExactDeadline()
    {
        Flight flight = new Flight("deadline", 0f, 10f);
        AirportSimulation simulation = new AirportSimulation(new[] { flight });

        simulation.Tick(9f);
        AssertStatus(flight, FlightStatus.Servicing, "flight should still be serviceable before deadline");
        Assert(simulation.TryAdvance(flight, ServiceKind.Meals, 0.25f),
            "service should be accepted before the deadline");
        simulation.Tick(1f);
        AssertNear(10f, simulation.Elapsed, 0.0001f, "tick should land exactly on the deadline");
        AssertStatus(flight, FlightStatus.Missed, "deadline should miss at the exact boundary");
        Assert(!simulation.TryAdvance(flight, ServiceKind.Meals, 1f),
            "service should be rejected at and after the deadline");
        AssertNear(0.25f, flight.Progress[(int)ServiceKind.Meals], 0.0001f,
            "work before the deadline should remain on the missed flight");
    }

    private static void TestEndShiftFreezesState()
    {
        Flight active = new Flight("active", 0f, 100f);
        Flight future = new Flight("future", 20f, 100f);
        AirportSimulation simulation = new AirportSimulation(new[] { active, future });

        Assert(simulation.TryAdvance(active, ServiceKind.Meals, 0.4f), "partial active work should be accepted");
        simulation.Tick(5f);
        float elapsedBeforeEnd = simulation.Elapsed;
        int scoreBeforeEnd = simulation.Score;
        int tasksBeforeEnd = simulation.CompletedTaskCount;
        simulation.EndShift();

        Assert(simulation.Finished, "shift should be marked finished");
        AssertNear(0f, simulation.Remaining, 0.0001f, "finished shift should have no remaining time");
        AssertNear(elapsedBeforeEnd, simulation.Elapsed, 0.0001f, "manual end should freeze elapsed time");
        AssertStatus(active, FlightStatus.Missed, "servicing flight should be marked missed at end");
        AssertStatus(future, FlightStatus.Missed, "scheduled flight should be marked missed at end");
        AssertEqual(-1, active.Stand, "active flight stand should be released at end");
        AssertEqual(-1, future.Stand, "scheduled flight stand should remain free at end");
        AssertEqual(2, simulation.MissedCount, "both unfinished flights should be missed once");
        AssertEqual(scoreBeforeEnd, simulation.Score, "ending should preserve score");
        AssertEqual(tasksBeforeEnd, simulation.CompletedTaskCount, "ending should preserve task count");
        AssertNear(0.4f, active.Progress[(int)ServiceKind.Meals], 0.0001f,
            "partial contribution should remain after ending");

        simulation.Tick(100f);
        AssertNear(elapsedBeforeEnd, simulation.Elapsed, 0.0001f, "tick after end should be ignored");
        Assert(!simulation.ReturnArrivalBags(active), "bag return after end should be rejected");
        Assert(!simulation.TryAdvance(active, ServiceKind.Meals, 0.6f), "service after end should be rejected");
        simulation.EndShift();
        AssertEqual(2, simulation.MissedCount, "repeated EndShift should not duplicate misses");
        AssertEqual(scoreBeforeEnd, simulation.Score, "repeated EndShift should not change score");
        AssertEqual("Shift ended", simulation.LastEvent, "end event should remain stable");
    }

    private static void TestPartialContributionAndNoRepeatScore()
    {
        Flight flight = new Flight("partial", 0f, 100f);
        AirportSimulation simulation = new AirportSimulation(new[] { flight });

        Assert(simulation.TryAdvance(flight, ServiceKind.Meals, 0.25f), "first partial contribution should apply");
        AssertNear(0.25f, flight.Progress[(int)ServiceKind.Meals], 0.0001f,
            "first partial contribution should be retained");
        AssertEqual(0, simulation.CompletedTaskCount, "partial task should not count before completion");
        AssertEqual(0, simulation.Score, "partial task should not award score");
        simulation.Tick(5f);
        AssertNear(0.25f, flight.Progress[(int)ServiceKind.Meals], 0.0001f,
            "partial contribution should survive a tick");

        Assert(simulation.TryAdvance(flight, ServiceKind.Meals, 0.75f), "remaining contribution should complete task");
        AssertNear(1f, flight.Progress[(int)ServiceKind.Meals], 0.0001f, "task should clamp at one");
        AssertEqual(1, simulation.CompletedTaskCount, "task should count once at completion");
        AssertEqual(50, simulation.Score, "task should award fifty points once");
        Assert(!simulation.TryAdvance(flight, ServiceKind.Meals, 0.1f), "completed task should reject more work");
        AssertEqual(1, simulation.CompletedTaskCount, "repeating a completed task should not count again");
        AssertEqual(50, simulation.Score, "repeating a completed task should not score again");

        Assert(simulation.TryAdvance(flight, ServiceKind.Fuel, 2f), "large contribution should be accepted and clamped");
        AssertNear(1f, flight.Progress[(int)ServiceKind.Fuel], 0.0001f, "large contribution should clamp at one");
        AssertEqual(2, simulation.CompletedTaskCount, "clamped completion should count once");
        AssertEqual(100, simulation.Score, "clamped completion should award one task bonus");
    }

    private static void TestCompleteFlightDeparts()
    {
        Flight flight = new Flight("complete", 0f, 100f);
        AirportSimulation simulation = new AirportSimulation(new[] { flight });

        CompleteFlight(simulation, flight);
        AssertStatus(flight, FlightStatus.Departed, "all four services should depart the flight");
        AssertEqual(-1, flight.Stand, "departed flight should release its stand");
        AssertEqual(1, simulation.DepartedCount, "one flight should depart");
        AssertEqual(0, simulation.MissedCount, "completed flight should not miss");
        AssertEqual(4, simulation.CompletedTaskCount, "four services should count as four tasks");
        AssertEqual(450, simulation.Score, "four task bonuses plus departure and time bonus should score 450");
        AssertEqual(null, simulation.ActiveAtStand(0), "departed flight should no longer occupy its stand");
        Assert(!simulation.TryAdvance(flight, ServiceKind.Meals, 1f), "departed flight should reject further work");
        Assert(!simulation.ReturnArrivalBags(flight), "departed flight should reject bag return");
    }

    private static void TestStarThresholds()
    {
        Flight[] flights = CreateFlights(100f, 100f, 100f, 100f);
        AirportSimulation simulation = new AirportSimulation(flights);

        AssertEqual(0, simulation.Stars, "zero departures should be zero stars");
        CompleteFlight(simulation, flights[0]);
        AssertEqual(1, simulation.Stars, "one departure should be one star");
        CompleteFlight(simulation, flights[1]);
        AssertEqual(2, simulation.Stars, "two departures should be two stars");
        CompleteFlight(simulation, flights[2]);
        AssertEqual(2, simulation.Stars, "three departures should remain two stars");
        CompleteFlight(simulation, flights[3]);
        AssertEqual(3, simulation.Stars, "four departures should be three stars");
        AssertEqual(4, simulation.DepartedCount, "four flights should depart");
    }

    private static void TestEndlessEmptyFlightTable()
    {
        AirportSimulation simulation = new AirportSimulation(new Flight[0]);
        simulation.Endless = true;

        AssertEqual(0, simulation.Flights.Count, "empty flight table should stay empty");
        Assert(!simulation.Finished, "endless shift should not finish at construction");

        // Tick 照走提供时间基：跨过 300s 结算点也永不 Finished。
        simulation.Tick(AirportSimulation.ShiftDuration + 60f);
        AssertNear(AirportSimulation.ShiftDuration + 60f, simulation.Elapsed, 0.001f,
            "endless tick should run past the normal settlement");
        Assert(!simulation.Finished, "endless shift should skip the 300s settlement");
        Assert(simulation.Remaining <= 0f, "endless remaining clamps at zero");
        AssertEqual(0, simulation.MissedCount, "no flights means no misses");
        AssertEqual("Shift started", simulation.LastEvent, "no events should fire on an empty table");

        for (int i = 0; i < 10; i++)
        {
            simulation.Tick(30f);
        }
        AssertNear(AirportSimulation.ShiftDuration + 360f, simulation.Elapsed, 0.001f,
            "endless ticks should keep accumulating the time base");
        Assert(!simulation.Finished, "endless shift should still be running");

        // 对照：非 Endless 空表在 300s 正常结算。
        AirportSimulation control = new AirportSimulation(new Flight[0]);
        control.Tick(AirportSimulation.ShiftDuration + 1f);
        Assert(control.Finished, "non-endless shift should settle at 300s even with an empty table");
    }
}
