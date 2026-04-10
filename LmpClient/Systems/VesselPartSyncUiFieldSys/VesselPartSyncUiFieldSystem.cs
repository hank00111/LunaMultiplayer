using LmpClient.Base;
using LmpClient.Events;
using LmpClient.Systems.LagDiag;
using LmpClient.Systems.TimeSync;
using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace LmpClient.Systems.VesselPartSyncUiFieldSys
{
    /// <summary>
    /// This class sends the changes in the UI fields of a part module. An example of a UI field would be the "thrust percentage" of an engine
    /// </summary>
    public class VesselPartSyncUiFieldSystem : MessageSystem<VesselPartSyncUiFieldSystem, VesselPartSyncUiFieldMessageSender, VesselPartSyncUiFieldMessageHandler>
    {
        #region Fields & properties

        public bool PartSyncSystemReady => Enabled && HighLogic.LoadedScene >= GameScenes.FLIGHT && Time.timeSinceLevelLoad > 1f;

        private VesselPartSyncUiFieldEvents VesselPartModuleSyncUiFieldEvents { get; } = new VesselPartSyncUiFieldEvents();

        public ConcurrentDictionary<Guid, VesselPartSyncUiFieldQueue> VesselPartsUiFieldsSyncs { get; } = new ConcurrentDictionary<Guid, VesselPartSyncUiFieldQueue>();

        private readonly System.Diagnostics.Stopwatch _drainStopwatch = new System.Diagnostics.Stopwatch();

        #endregion

        #region Base overrides        

        protected override bool ProcessMessagesInUnityThread => false;

        public override string SystemName { get; } = nameof(VesselPartSyncUiFieldSystem);

        protected override void OnEnabled()
        {
            base.OnEnabled();

            LockEvent.onLockAcquire.Add(VesselPartModuleSyncUiFieldEvents.LockAcquire);

            SetupRoutine(new RoutineDefinition(250, RoutineExecution.Update, ProcessVesselPartUiFieldsSyncs));
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();

            LockEvent.onLockAcquire.Remove(VesselPartModuleSyncUiFieldEvents.LockAcquire);

            VesselPartsUiFieldsSyncs.Clear();
        }

        #endregion

        #region Update routines

        // P3b patch: timeout forced release. See VesselPartSyncFieldSystem for
        // derivation from TimeSyncSystem.MaxPhysicsClockMsError (3.5s) + 1.5s margin.
        private const double MaxAgeSeconds = 5.0;

        private void ProcessVesselPartUiFieldsSyncs()
        {
            if (HighLogic.LoadedScene < GameScenes.SPACECENTER) return;

            _drainStopwatch.Restart();
            var processed = 0;

            foreach (var keyVal in VesselPartsUiFieldsSyncs)
            {
                while (keyVal.Value.TryPeek(out var update) &&
                       (update.GameTime <= TimeSyncSystem.UniversalTime ||
                        update.GameTime - TimeSyncSystem.UniversalTime > MaxAgeSeconds))
                {
                    keyVal.Value.TryDequeue(out update);
                    update.ProcessPartMethodSync();
                    keyVal.Value.Recycle(update);
                    processed++;
                }
            }

            _drainStopwatch.Stop();
            LagDiagSystem.Singleton.ReportDrain("PartSyncUi", processed, _drainStopwatch.Elapsed.TotalMilliseconds);
        }

        #endregion

        #region Public methods

        /// <summary>
        /// Removes a vessel from the system
        /// </summary>
        public void RemoveVessel(Guid vesselId)
        {
            VesselPartsUiFieldsSyncs.TryRemove(vesselId, out _);
        }

        #endregion
    }
}
