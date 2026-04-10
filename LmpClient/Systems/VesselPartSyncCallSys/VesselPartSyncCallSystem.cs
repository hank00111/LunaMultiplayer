using System;
using System.Collections.Concurrent;
using LmpClient.Base;
using LmpClient.Events;
using LmpClient.Systems.LagDiag;
using LmpClient.Systems.TimeSync;
using UnityEngine;

namespace LmpClient.Systems.VesselPartSyncCallSys
{
    /// <summary>
    /// This system sends the part module calls to the other players. 
    /// An example would be selecting "Activate engine" action when you right click on an engine and press that part action
    /// Another would be "Extend" in the retractable ladder part
    /// </summary>
    public class VesselPartSyncCallSystem : MessageSystem<VesselPartSyncCallSystem, VesselPartSyncCallMessageSender, VesselPartSyncCallMessageHandler>
    {
        #region Fields & properties

        public bool PartSyncSystemReady => Enabled && HighLogic.LoadedScene >= GameScenes.FLIGHT && Time.timeSinceLevelLoad > 1f;

        private VesselPartSyncCallEvents VesselPartModuleSyncCallEvents { get; } = new VesselPartSyncCallEvents();

        public ConcurrentDictionary<Guid, VesselPartSyncCallQueue> VesselPartsSyncs { get; } = new ConcurrentDictionary<Guid, VesselPartSyncCallQueue>();

        private readonly System.Diagnostics.Stopwatch _drainStopwatch = new System.Diagnostics.Stopwatch();

        #endregion

        #region Base overrides        

        protected override bool ProcessMessagesInUnityThread => false;

        public override string SystemName { get; } = nameof(VesselPartSyncCallSystem);

        protected override void OnEnabled()
        {
            base.OnEnabled();
            PartModuleEvent.onPartModuleMethodCalling.Add(VesselPartModuleSyncCallEvents.PartModuleMethodCalled);
            SetupRoutine(new RoutineDefinition(250, RoutineExecution.Update, ProcessVesselPartSyncCalls));
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();
            PartModuleEvent.onPartModuleMethodCalling.Remove(VesselPartModuleSyncCallEvents.PartModuleMethodCalled);

            VesselPartsSyncs.Clear();
        }

        #endregion

        #region Update routines

        // P1 patch: timeout forced release. See VesselPartSyncFieldSystem for full
        // rationale and derivation from TimeSyncSystem.MaxPhysicsClockMsError.
        private const double MaxAgeSeconds = 5.0;

        private void ProcessVesselPartSyncCalls()
        {
            _drainStopwatch.Restart();
            var processed = 0;

            foreach (var keyVal in VesselPartsSyncs)
            {
                while (keyVal.Value.TryPeek(out var update) &&
                       (update.GameTime <= TimeSyncSystem.UniversalTime ||
                        update.GameTime - TimeSyncSystem.UniversalTime > MaxAgeSeconds))
                {
                    keyVal.Value.TryDequeue(out update);
                    update.ProcessPartMethodCallSync();
                    keyVal.Value.Recycle(update);
                    processed++;
                }
            }

            _drainStopwatch.Stop();
            LagDiagSystem.Singleton.ReportDrain("PartSyncCall", processed, _drainStopwatch.Elapsed.TotalMilliseconds);
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Removes a vessel from the system
        /// </summary>
        public void RemoveVessel(Guid vesselId)
        {
            VesselPartsSyncs.TryRemove(vesselId, out _);
        }

        #endregion
    }
}
