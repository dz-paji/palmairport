using System;

namespace IslandAirport
{
    /// <summary>
    /// The three phases of the deliberately small, one-flight game loop.
    /// Arriving and Departing are timed by this controller. Servicing is
    /// advanced only by callers operating on <see cref="Simulation"/>.
    /// </summary>
    public enum CyclePhase
    {
        Arriving,
        Servicing,
        Departing
    }

    /// <summary>
    /// Runs one flight at a time without applying the five-minute shift clock.
    /// The controller owns the arrival/departure animation clocks while the
    /// existing AirportSimulation remains the source of truth for service
    /// prerequisites and task completion.
    /// </summary>
    public sealed class SingleFlightCycle
    {
        public const float ArrivalDuration = 8f;
        public const float DepartureDuration = 10f;

        public SingleFlightCycle()
        {
            Reset();
        }

        public AirportSimulation Simulation { get; private set; }

        public Flight Current { get; private set; }

        public CyclePhase Phase { get; private set; }

        /// <summary>
        /// Elapsed seconds in the current timed phase. It is reset to zero
        /// whenever the controller enters a new phase.
        /// </summary>
        public float PhaseProgress { get; private set; }

        public int CompletedFlights { get; private set; }

        /// <summary>
        /// One-based sequence number of the current flight.
        /// </summary>
        public int Sequence { get; private set; }

        /// <summary>
        /// Which ramp the current flight visually uses. Flights rotate through
        /// the three level-1 stands. The simulation itself only tracks a
        /// single flight (always stand 0 internally) — the visual stand is
        /// deliberately decoupled from the simulation's stand bookkeeping.
        /// </summary>
        public int CurrentStand { get { return (Sequence - 1) % AirportSimulation.StandCount; } }

        /// <summary>
        /// Recreate the first flight and clear the completed-flight count.
        /// </summary>
        public void Reset()
        {
            CompletedFlights = 0;
            Sequence = 1;
            Phase = CyclePhase.Arriving;
            PhaseProgress = 0f;
            CreateCurrentFlight();
        }

        /// <summary>
        /// Advance timed phases. A servicing flight never receives a
        /// simulation tick here, so a caller may take as long as it needs to
        /// perform the four tasks. If a large dt crosses a timed boundary, the
        /// controller may finish those timed phases, but it stops as soon as
        /// it reaches the next servicing phase.
        /// </summary>
        public void Tick(float dt)
        {
            if (dt < 0f || float.IsNaN(dt) || float.IsInfinity(dt))
            {
                return;
            }

            float remaining = dt;

            while (true)
            {
                // A flight can become Departed through an external call to
                // Simulation.TryAdvance. Detect that transition before
                // consuming time, including for Tick(0).
                if (Phase == CyclePhase.Servicing)
                {
                    if (Current.Status == FlightStatus.Departed)
                    {
                        Phase = CyclePhase.Departing;
                        PhaseProgress = 0f;
                        continue;
                    }

                    // Servicing is intentionally not timed and cannot be
                    // skipped by a large controller tick.
                    return;
                }

                if (remaining <= 0f)
                {
                    return;
                }

                if (Phase == CyclePhase.Arriving)
                {
                    float untilArrival = ArrivalDuration - PhaseProgress;
                    if (remaining < untilArrival)
                    {
                        PhaseProgress += remaining;
                        return;
                    }

                    PhaseProgress = ArrivalDuration;
                    remaining -= untilArrival;

                    // The simulation's flight is scheduled at t=3. Calling
                    // it with exactly three seconds assigns that flight to
                    // the first (and only) stand.
                    Simulation.Tick(ArrivalDuration);
                    Phase = CyclePhase.Servicing;
                    PhaseProgress = 0f;
                    return;
                }

                // The only remaining timed phase is Departing.
                float untilDeparture = DepartureDuration - PhaseProgress;
                if (remaining < untilDeparture)
                {
                    PhaseProgress += remaining;
                    return;
                }

                PhaseProgress = DepartureDuration;
                remaining -= untilDeparture;
                CompletedFlights++;
                Sequence++;
                Phase = CyclePhase.Arriving;
                PhaseProgress = 0f;
                CreateCurrentFlight();

                // Continue with any leftover time. The loop returns as soon
                // as this new flight reaches Servicing, so a large dt cannot
                // perform human-controlled work implicitly.
            }
        }

        private void CreateCurrentFlight()
        {
            string suffix = Sequence.ToString();
            Current = new Flight("SINGLE-" + suffix, "DEST-" + suffix,
                ArrivalDuration, float.MaxValue);
            Simulation = new AirportSimulation(new[] { Current });
        }
    }
}
