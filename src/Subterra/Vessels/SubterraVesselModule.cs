using UnityEngine;

namespace Subterra
{
    /// <summary>
    /// Keeps each vessel's <see cref="UndergroundState"/> (5 Hz) and runs its <see cref="LoadGuard"/>.
    /// </summary>
    public sealed class SubterraVesselModule : VesselModule
    {
        private const float Interval = 0.2f;
        private float nextUpdate;
        private UndergroundState state;
        private float stateTime = float.NegativeInfinity;

        public LoadGuard Guard { get; } = new LoadGuard();

        /// <summary>The vessel's state, refreshed if older than 0.2 s.</summary>
        public UndergroundState State
        {
            get
            {
                if (Time.realtimeSinceStartup - stateTime > Interval)
                {
                    Refresh();
                }

                return state;
            }
        }

        public override Activation GetActivation() => Activation.FlightScene | Activation.LoadedOrUnloaded;

        /// <summary>The module of <paramref name="vessel"/>, or null.</summary>
        public static SubterraVesselModule Of(Vessel vessel) => vessel == null ? null : vessel.FindVesselModuleImplementing<SubterraVesselModule>();

        public void Refresh()
        {
            stateTime = Time.realtimeSinceStartup;
            state = UndergroundTracker.Compute(vessel.mainBody, vessel.GetWorldPos3D(), true);
        }

        private void Update()
        {
            if (vessel == null || !HighLogic.LoadedSceneIsFlight)
            {
                return;
            }

            using (SubterraClock.Measure("SubterraVesselModule"))
            {
                if (Time.realtimeSinceStartup >= nextUpdate)
                {
                    nextUpdate = Time.realtimeSinceStartup + Interval;
                    Refresh();
                }

                if (vessel.loaded)
                {
                    Guard.Update(vessel, state);
                }
                else
                {
                    Guard.Release(vessel);
                }
            }
        }

        private void OnDestroy()
        {
            if (vessel != null)
            {
                Guard.Release(vessel);
            }
        }
    }
}
