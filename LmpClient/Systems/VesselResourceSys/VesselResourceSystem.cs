using System;
using System.Collections.Concurrent;
using LmpClient.Base;
using LmpClient.Systems.LagDiag;
using LmpClient.Systems.TimeSync;
using LmpClient.VesselUtilities;

namespace LmpClient.Systems.VesselResourceSys
{
    public class VesselResourceSystem : MessageSystem<VesselResourceSystem, VesselResourceMessageSender, VesselResourceMessageHandler>
    {
        #region Fields & properties

        public ConcurrentDictionary<Guid, VesselResourceQueue> VesselResources { get; } = new ConcurrentDictionary<Guid, VesselResourceQueue>();

        private readonly System.Diagnostics.Stopwatch _drainStopwatch = new System.Diagnostics.Stopwatch();

        #endregion

        #region Base overrides        

        protected override bool ProcessMessagesInUnityThread => false;

        public override string SystemName { get; } = nameof(VesselResourceSystem);

        protected override void OnEnabled()
        {
            base.OnEnabled();
            SetupRoutine(new RoutineDefinition(2500, RoutineExecution.Update, SendVesselResources));
            SetupRoutine(new RoutineDefinition(2500, RoutineExecution.Update, ProcessVesselResources));
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();
            VesselResources.Clear();
        }

        #endregion

        #region Update routines

        // P2b patch: timeout forced release. See VesselPartSyncFieldSystem for
        // derivation from TimeSyncSystem.MaxPhysicsClockMsError (3.5s) + 1.5s margin.
        private const double MaxAgeSeconds = 5.0;

        private void ProcessVesselResources()
        {
            if (HighLogic.LoadedScene < GameScenes.SPACECENTER) return;

            _drainStopwatch.Restart();
            var processed = 0;

            foreach (var keyVal in VesselResources)
            {
                while (keyVal.Value.TryPeek(out var update) &&
                       (update.GameTime <= TimeSyncSystem.UniversalTime ||
                        update.GameTime - TimeSyncSystem.UniversalTime > MaxAgeSeconds))
                {
                    keyVal.Value.TryDequeue(out update);
                    update.ProcessVesselResource();
                    keyVal.Value.Recycle(update);
                    processed++;
                }
            }

            _drainStopwatch.Stop();
            LagDiagSystem.Singleton.ReportDrain("Resource", processed, _drainStopwatch.Elapsed.TotalMilliseconds);
        }

        private void SendVesselResources()
        {
            if (FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.loaded && !VesselCommon.IsSpectating)
            {
                MessageSender.SendVesselResources(FlightGlobals.ActiveVessel);
            }
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Removes a vessel from the system
        /// </summary>
        public void RemoveVessel(Guid vesselId)
        {
            VesselResources.TryRemove(vesselId, out _);
        }

        #endregion
    }
}
