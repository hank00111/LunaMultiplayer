using LmpClient.Base;
using LmpClient.Systems.LagDiag;
using LmpClient.Systems.TimeSync;
using System;
using System.Collections.Concurrent;

namespace LmpClient.Systems.VesselFairingsSys
{
    /// <summary>
    /// This class syncs the fairings between players
    /// </summary>
    public class VesselFairingsSystem : MessageSystem<VesselFairingsSystem, VesselFairingsMessageSender, VesselFairingsMessageHandler>
    {
        #region Fields & properties

        public ConcurrentDictionary<Guid, VesselFairingQueue> VesselFairings { get; } = new ConcurrentDictionary<Guid, VesselFairingQueue>();
        private VesselFairingEvents VesselFairingEvents { get; } = new VesselFairingEvents();

        private readonly System.Diagnostics.Stopwatch _drainStopwatch = new System.Diagnostics.Stopwatch();

        #endregion

        #region Base overrides        

        protected override bool ProcessMessagesInUnityThread => false;

        public override string SystemName { get; } = nameof(VesselFairingsSystem);

        protected override void OnEnabled()
        {
            base.OnEnabled();
            GameEvents.onFairingsDeployed.Add(VesselFairingEvents.FairingsDeployed);
            SetupRoutine(new RoutineDefinition(1500, RoutineExecution.Update, ProcessVesselFairings));
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();
            GameEvents.onFairingsDeployed.Remove(VesselFairingEvents.FairingsDeployed);
            VesselFairings.Clear();
        }

        #endregion

        #region Update routines

        private void ProcessVesselFairings()
        {
            _drainStopwatch.Restart();
            var processed = 0;

            foreach (var keyVal in VesselFairings)
            {
                while (keyVal.Value.TryPeek(out var update) && update.GameTime <= TimeSyncSystem.UniversalTime)
                {
                    keyVal.Value.TryDequeue(out update);
                    update.ProcessFairing();
                    keyVal.Value.Recycle(update);
                    processed++;
                }
            }

            _drainStopwatch.Stop();
            LagDiagSystem.Singleton.ReportDrain("Fairings", processed, _drainStopwatch.Elapsed.TotalMilliseconds);
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Removes a vessel from the system
        /// </summary>
        public void RemoveVessel(Guid vesselId)
        {
            VesselFairings.TryRemove(vesselId, out _);
        }

        #endregion
    }
}
