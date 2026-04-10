using System;
using System.Collections.Concurrent;
using LmpClient.Base;
using LmpClient.Events;
using LmpClient.Systems.LagDiag;
using LmpClient.Systems.TimeSync;

namespace LmpClient.Systems.VesselActionGroupSys
{
    /// <summary>
    /// This class sends and processes the action groups
    /// </summary>
    public class VesselActionGroupSystem : MessageSystem<VesselActionGroupSystem, VesselActionGroupMessageSender, VesselActionGroupMessageHandler>
    {
        #region Fields & properties

        public ConcurrentDictionary<Guid, VesselActionGroupQueue> VesselActionGroups { get; } = new ConcurrentDictionary<Guid, VesselActionGroupQueue>();

        public static VesselActionGroupEvents VesselActionGroupEvents { get; } = new VesselActionGroupEvents();

        private readonly System.Diagnostics.Stopwatch _drainStopwatch = new System.Diagnostics.Stopwatch();

        #endregion

        #region Base overrides

        protected override bool ProcessMessagesInUnityThread => false;

        public override string SystemName { get; } = nameof(VesselActionGroupSystem);

        protected override void OnEnabled()
        {
            base.OnEnabled();
            SetupRoutine(new RoutineDefinition(500, RoutineExecution.Update, ProcessVesselActionGroups));

            ActionGroupEvent.onActionGroupFired.Add(VesselActionGroupEvents.ActionGroupFired);
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();
            VesselActionGroups.Clear();

            ActionGroupEvent.onActionGroupFired.Remove(VesselActionGroupEvents.ActionGroupFired);
        }

        #endregion

        #region Update routines

        // P2c patch: timeout forced release. See VesselPartSyncFieldSystem for
        // derivation from TimeSyncSystem.MaxPhysicsClockMsError (3.5s) + 1.5s margin.
        private const double MaxAgeSeconds = 5.0;

        private void ProcessVesselActionGroups()
        {
            _drainStopwatch.Restart();
            var processed = 0;

            foreach (var keyVal in VesselActionGroups)
            {
                while (keyVal.Value.TryPeek(out var update) &&
                       (update.GameTime <= TimeSyncSystem.UniversalTime ||
                        update.GameTime - TimeSyncSystem.UniversalTime > MaxAgeSeconds))
                {
                    keyVal.Value.TryDequeue(out update);
                    update.ProcessActionGroup();
                    keyVal.Value.Recycle(update);
                    processed++;
                }
            }

            _drainStopwatch.Stop();
            LagDiagSystem.Singleton.ReportDrain("ActionGroup", processed, _drainStopwatch.Elapsed.TotalMilliseconds);
        }

        #endregion
    }
}
