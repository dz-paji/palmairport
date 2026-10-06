using System;
using IslandAirport;

/// <summary>
/// Dependency-free console coverage for SingleFlightCycle. Run this with the
/// same mcs and mono toolchain used by the AirportSimulation tests.
/// </summary>
public static class SingleFlightCycleTests
{
    private static int _passed;
    private static int _failed;
    private static int _assertions;

    public static void Main()
    {
        Run("starts with one arriving flight", TestStartsWithOneArrivingFlight);
        Run("arrival waits and then assigns stand zero", TestArrivalBoundary);
        Run("service requires all four tasks and arrival bags", TestServiceTasks);
        Run("incomplete service cannot depart", TestIncompleteServiceCannotDepart);
        Run("completion animates and creates a fresh flight", TestCompletionAndNextFlight);
        Run("second flight resets task state", TestSecondFlightTaskState);
        Run("servicing does not time out", TestServicingDoesNotTimeOut);
        Run("large ticks stop at human servicing", TestLargeTicksStopAtServicing);
        Run("invalid ticks are ignored", TestInvalidTicks);
        Run("reset returns to the first flight", TestReset);

        Console.WriteLine("SingleFlightCycle tests: {0} passed, {1} failed, {2} assertions.",
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

    private static void Arrive(SingleFlightCycle cycle)
    {
        cycle.Tick(SingleFlightCycle.ArrivalDuration);
        AssertEqual(CyclePhase.Servicing, cycle.Phase, "flight should be servicing after arrival");
        AssertStatus(cycle.Current, FlightStatus.Servicing, "arrived flight");
        AssertEqual(0, cycle.Current.Stand, "single flight should use stand zero");
    }

    private static void Complete(SingleFlightCycle cycle)
    {
        Flight flight = cycle.Current;
        Assert(cycle.Simulation.ReturnArrivalBags(flight), "arrival baggage should be returnable");
        Assert(cycle.Simulation.TryAdvance(flight, ServiceKind.Meals, 1f), "meals should complete");
        Assert(cycle.Simulation.TryAdvance(flight, ServiceKind.Fuel, 1f), "fuel should complete");
        Assert(cycle.Simulation.TryAdvance(flight, ServiceKind.Baggage, 1f), "baggage should complete");
        Assert(cycle.Simulation.TryAdvance(flight, ServiceKind.Boarding, 1f), "boarding should complete");
        AssertStatus(flight, FlightStatus.Departed, "completed flight");
    }

    private static void TestStartsWithOneArrivingFlight()
    {
        SingleFlightCycle cycle = new SingleFlightCycle();

        AssertEqual(CyclePhase.Arriving, cycle.Phase, "new cycle phase");
        AssertNear(0f, cycle.PhaseProgress, 0.0001f, "new cycle progress");
        AssertEqual(0, cycle.CompletedFlights, "new cycle completed count");
        AssertEqual(1, cycle.Sequence, "new cycle sequence");
        AssertEqual(1, cycle.Simulation.Flights.Count, "one flight should be scheduled");
        AssertEqual(cycle.Current, cycle.Simulation.Flights[0], "current flight should be in simulation");
        AssertStatus(cycle.Current, FlightStatus.Scheduled, "flight should begin scheduled");
        AssertEqual(-1, cycle.Current.Stand, "flight should have no stand before arrival");
        AssertNear(SingleFlightCycle.ArrivalDuration, cycle.Current.ArrivalTime, 0.0001f, "arrival should match the full taxi-in duration");
        AssertEqual(float.MaxValue, cycle.Current.Deadline, "single-flight deadline should use float.MaxValue");
    }

    private static void TestArrivalBoundary()
    {
        SingleFlightCycle cycle = new SingleFlightCycle();
        cycle.Tick(1f);
        AssertEqual(CyclePhase.Arriving, cycle.Phase, "flight should still be arriving");
        AssertNear(1f, cycle.PhaseProgress, 0.0001f, "arrival progress should be elapsed seconds");
        AssertStatus(cycle.Current, FlightStatus.Scheduled, "scheduled flight should not accept tasks");
        Assert(!cycle.Simulation.ReturnArrivalBags(cycle.Current), "arrival bags should be unavailable before landing");
        Assert(!cycle.Simulation.TryAdvance(cycle.Current, ServiceKind.Meals, 1f),
            "service should be unavailable before landing");

        cycle.Tick(SingleFlightCycle.ArrivalDuration - 1f);
        AssertEqual(CyclePhase.Servicing, cycle.Phase, "exact arrival boundary should land flight");
        AssertNear(0f, cycle.PhaseProgress, 0.0001f, "service phase should start at zero");
        AssertStatus(cycle.Current, FlightStatus.Servicing, "flight should be servicing after the arrival duration");
        AssertEqual(0, cycle.Current.Stand, "flight should occupy stand zero");
        AssertEqual(SingleFlightCycle.ArrivalDuration, cycle.Simulation.Elapsed, "arrival should advance simulation exactly once");
    }

    private static void TestServiceTasks()
    {
        SingleFlightCycle cycle = new SingleFlightCycle();
        Arrive(cycle);
        Flight flight = cycle.Current;

        Assert(!cycle.Simulation.TryAdvance(flight, ServiceKind.Baggage, 1f),
            "baggage should require returned arrival bags");
        Assert(cycle.Simulation.ReturnArrivalBags(flight), "arrival bags should be returned first");
        Assert(cycle.Simulation.TryAdvance(flight, ServiceKind.Meals, 1f), "meals should complete");
        Assert(cycle.Simulation.TryAdvance(flight, ServiceKind.Fuel, 1f), "fuel should complete");
        Assert(cycle.Simulation.TryAdvance(flight, ServiceKind.Baggage, 1f), "baggage should complete");
        Assert(cycle.Simulation.TryAdvance(flight, ServiceKind.Boarding, 1f), "boarding should complete");
        AssertStatus(flight, FlightStatus.Departed, "all four tasks should depart flight");
        AssertEqual(4, cycle.Simulation.CompletedTaskCount, "all four tasks should be counted");
    }

    private static void TestIncompleteServiceCannotDepart()
    {
        SingleFlightCycle cycle = new SingleFlightCycle();
        Arrive(cycle);
        Flight flight = cycle.Current;
        Assert(cycle.Simulation.ReturnArrivalBags(flight), "arrival bags should be returned");
        Assert(cycle.Simulation.TryAdvance(flight, ServiceKind.Meals, 1f), "meals should complete");
        Assert(cycle.Simulation.TryAdvance(flight, ServiceKind.Fuel, 1f), "fuel should complete");

        cycle.Tick(1000f);
        AssertEqual(CyclePhase.Servicing, cycle.Phase, "large service tick should not depart incomplete flight");
        AssertStatus(flight, FlightStatus.Servicing, "incomplete flight should remain servicing");
        AssertEqual(0, cycle.CompletedFlights, "incomplete flight should not count as completed");
        Assert(!cycle.Simulation.TryAdvance(flight, ServiceKind.Boarding, 0f), "zero work should be rejected");
    }

    private static void TestCompletionAndNextFlight()
    {
        SingleFlightCycle cycle = new SingleFlightCycle();
        Arrive(cycle);
        Complete(cycle);
        Flight first = cycle.Current;

        cycle.Tick(0f);
        AssertEqual(CyclePhase.Departing, cycle.Phase, "departed flight should enter departure phase");
        AssertNear(0f, cycle.PhaseProgress, 0.0001f, "departure should start at zero");
        AssertEqual(0, cycle.CompletedFlights, "animation should precede completion count");

        cycle.Tick(SingleFlightCycle.DepartureDuration / 2f);
        AssertEqual(CyclePhase.Departing, cycle.Phase, "departure should still be animating");
        AssertNear(SingleFlightCycle.DepartureDuration / 2f, cycle.PhaseProgress, 0.0001f, "departure progress");
        cycle.Tick(SingleFlightCycle.DepartureDuration / 2f);

        AssertEqual(CyclePhase.Arriving, cycle.Phase, "next flight should begin arriving");
        AssertNear(0f, cycle.PhaseProgress, 0.0001f, "next arrival should start at zero");
        AssertEqual(1, cycle.CompletedFlights, "one flight should be completed after departure animation");
        AssertEqual(2, cycle.Sequence, "sequence should advance from one");
        Assert(cycle.Current != first, "next flight should be a new object");
        Assert(cycle.Current.Id != first.Id, "next flight should have a unique id");
        AssertEqual(1, cycle.Simulation.Flights.Count, "new simulation should contain one flight");
        AssertStatus(cycle.Current, FlightStatus.Scheduled, "next flight should start scheduled");
        AssertEqual(-1, cycle.Current.Stand, "next flight should not have a stand yet");
    }

    private static void TestSecondFlightTaskState()
    {
        SingleFlightCycle cycle = new SingleFlightCycle();
        Arrive(cycle);
        Complete(cycle);
        cycle.Tick(0f);
        cycle.Tick(SingleFlightCycle.DepartureDuration);
        Arrive(cycle);

        AssertEqual(1, cycle.CompletedFlights, "first completion should persist");
        AssertEqual(2, cycle.Sequence, "second sequence should be active");
        Assert(!cycle.Current.ArrivalBagsReturned, "second arrival bags should be fresh");
        for (int i = 0; i < cycle.Current.Progress.Length; i++)
        {
            AssertNear(0f, cycle.Current.Progress[i], 0.0001f,
                "second flight task progress should be reset " + i);
        }
        AssertEqual(0, cycle.Simulation.CompletedTaskCount, "second simulation task count should reset");
        AssertEqual(0, cycle.Simulation.DepartedCount, "second simulation departure count should reset");
    }

    private static void TestServicingDoesNotTimeOut()
    {
        SingleFlightCycle cycle = new SingleFlightCycle();
        Arrive(cycle);
        float elapsed = cycle.Simulation.Elapsed;
        cycle.Tick(1000000f);
        AssertEqual(CyclePhase.Servicing, cycle.Phase, "service should remain active indefinitely");
        AssertNear(elapsed, cycle.Simulation.Elapsed, 0.0001f,
            "controller should not advance simulation during service");
        AssertStatus(cycle.Current, FlightStatus.Servicing, "service should not time out");
    }

    private static void TestLargeTicksStopAtServicing()
    {
        SingleFlightCycle cycle = new SingleFlightCycle();
        cycle.Tick(1000000f);
        AssertEqual(CyclePhase.Servicing, cycle.Phase,
            "large arrival tick should stop at servicing");
        AssertNear(0f, cycle.PhaseProgress, 0.0001f,
            "large arrival tick should not consume service time");
        AssertEqual(SingleFlightCycle.ArrivalDuration, cycle.Simulation.Elapsed, "large arrival tick should use the configured landing event");

        Complete(cycle);
        cycle.Tick(1000000f);
        AssertEqual(CyclePhase.Servicing, cycle.Phase,
            "large post-completion tick should animate timed phases then stop at service");
        AssertEqual(1, cycle.CompletedFlights, "large post-completion tick should complete one flight");
        AssertEqual(2, cycle.Sequence, "large post-completion tick should create the next flight");
        AssertEqual(SingleFlightCycle.ArrivalDuration, cycle.Simulation.Elapsed, "new flight should have completed only its arrival timing");
        AssertStatus(cycle.Current, FlightStatus.Servicing, "new flight should be landed, awaiting tasks");
    }

    private static void TestInvalidTicks()
    {
        SingleFlightCycle cycle = new SingleFlightCycle();
        Flight original = cycle.Current;
        cycle.Tick(-1f);
        cycle.Tick(float.NaN);
        cycle.Tick(float.PositiveInfinity);
        cycle.Tick(float.NegativeInfinity);

        AssertEqual(CyclePhase.Arriving, cycle.Phase, "invalid ticks should preserve phase");
        AssertNear(0f, cycle.PhaseProgress, 0.0001f, "invalid ticks should preserve progress");
        AssertEqual(0, cycle.CompletedFlights, "invalid ticks should preserve completed count");
        AssertEqual(1, cycle.Sequence, "invalid ticks should preserve sequence");
        AssertEqual(original, cycle.Current, "invalid ticks should preserve current flight");
        AssertNear(0f, cycle.Simulation.Elapsed, 0.0001f, "invalid ticks should not advance simulation");
    }

    private static void TestReset()
    {
        SingleFlightCycle cycle = new SingleFlightCycle();
        Arrive(cycle);
        Complete(cycle);
        cycle.Tick(0f);
        cycle.Tick(SingleFlightCycle.DepartureDuration);
        Flight second = cycle.Current;
        cycle.Reset();

        AssertEqual(CyclePhase.Arriving, cycle.Phase, "reset phase");
        AssertNear(0f, cycle.PhaseProgress, 0.0001f, "reset progress");
        AssertEqual(0, cycle.CompletedFlights, "reset completed count");
        AssertEqual(1, cycle.Sequence, "reset sequence");
        Assert(cycle.Current != second, "reset should create a fresh current flight");
        AssertStatus(cycle.Current, FlightStatus.Scheduled, "reset flight should be scheduled");
        AssertEqual(1, cycle.Simulation.Flights.Count, "reset simulation should contain one flight");
    }
}
