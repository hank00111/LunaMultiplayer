using LmpClient.Base;
using LmpClient.Systems.LagDiag;
using LmpClient.Systems.TimeSync;
using LmpClient.VesselUtilities;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace LmpClient.Systems.VesselUpdateSys
{
    /// <summary>
    /// This class sends some parts of the vessel information to other players. We do it in another system as we don't want to send this information so often as
    /// the vessel position system and also we want to send it more oftenly than the vessel proto.
    /// </summary>
    public class VesselUpdateSystem : MessageSystem<VesselUpdateSystem, VesselUpdateMessageSender, VesselUpdateMessageHandler>
    {
        #region Fields & properties

        public bool VesselUpdateSystemReady => Enabled && FlightGlobals.ActiveVessel != null && HighLogic.LoadedScene == GameScenes.FLIGHT &&
                                              FlightGlobals.ready && FlightGlobals.ActiveVessel.loaded &&
                                              FlightGlobals.ActiveVessel.state != Vessel.State.DEAD &&
                                              FlightGlobals.ActiveVessel.vesselType != VesselType.Flag;

        private List<Vessel> SecondaryVesselsToUpdate { get; } = new List<Vessel>();
        private List<Vessel> AbandonedVesselsToUpdate { get; } = new List<Vessel>();

        public ConcurrentDictionary<Guid, VesselUpdateQueue> VesselUpdates { get; } = new ConcurrentDictionary<Guid, VesselUpdateQueue>();

        private readonly System.Diagnostics.Stopwatch _drainStopwatch = new System.Diagnostics.Stopwatch();

        #endregion

        #region Base overrides

        protected override bool ProcessMessagesInUnityThread => false;

        public override string SystemName { get; } = nameof(VesselUpdateSystem);

        protected override void OnEnabled()
        {
            base.OnEnabled();
            SetupRoutine(new RoutineDefinition(1500, RoutineExecution.Update, SendVesselUpdates));
            SetupRoutine(new RoutineDefinition(1500, RoutineExecution.Update, ProcessVesselUpdates));
            SetupRoutine(new RoutineDefinition(5000, RoutineExecution.Update, SendSecondaryVesselUpdates));
            //SetupRoutine(new RoutineDefinition(10000, RoutineExecution.Update, SendUnloadedSecondaryVesselUpdates));
        }

        protected override void OnDisabled()
        {
            base.OnDisabled();
            VesselUpdates.Clear();
        }

        #endregion

        #region Update routines

        // P2 patch: timeout forced release — same mechanism as P1 (PartSyncField/Call).
        // When GameTime - UniversalTime > 5.0s, force-dequeue and process immediately.
        // Derived from TimeSyncSystem.MaxPhysicsClockMsError (3500ms / 3.5s) + 1.5s margin.
        // See VesselPartSyncFieldSystem for full derivation.
        // Evidence: LagDiag Dump 5 captured Update=367 single-tick drain causing FPS<10.
        private const double MaxAgeSeconds = 5.0;

        private void ProcessVesselUpdates()
        {
            if (HighLogic.LoadedScene < GameScenes.SPACECENTER) return;

            _drainStopwatch.Restart();
            var processed = 0;

            foreach (var keyVal in VesselUpdates)
            {
                while (keyVal.Value.TryPeek(out var update) &&
                       (update.GameTime <= TimeSyncSystem.UniversalTime ||
                        update.GameTime - TimeSyncSystem.UniversalTime > MaxAgeSeconds))
                {
                    keyVal.Value.TryDequeue(out update);
                    update.ProcessVesselUpdate();
                    keyVal.Value.Recycle(update);
                    processed++;
                }
            }

            _drainStopwatch.Stop();
            LagDiagSystem.Singleton.ReportDrain("Update", processed, _drainStopwatch.Elapsed.TotalMilliseconds);
        }

        private void SendVesselUpdates()
        {
            if (!VesselCommon.IsSpectating && VesselUpdateSystemReady)
            {
                MessageSender.SendVesselUpdate(FlightGlobals.ActiveVessel);
            }
        }

        private void SendSecondaryVesselUpdates()
        {
            if (!VesselCommon.IsSpectating)
            {
                SecondaryVesselsToUpdate.Clear();
                SecondaryVesselsToUpdate.AddRange(VesselCommon.GetSecondaryVessels());

                for (var i = 0; i < SecondaryVesselsToUpdate.Count; i++)
                {
                    MessageSender.SendVesselUpdate(SecondaryVesselsToUpdate[i]);
                }
            }
        }

        private void SendUnloadedSecondaryVesselUpdates()
        {
            if (!VesselCommon.IsSpectating)
            {
                AbandonedVesselsToUpdate.Clear();
                AbandonedVesselsToUpdate.AddRange(VesselCommon.GetUnloadedSecondaryVessels());

                for (var i = 0; i < AbandonedVesselsToUpdate.Count; i++)
                {
                    MessageSender.SendVesselUpdate(AbandonedVesselsToUpdate[i]);
                }
            }
        }

        #endregion
    }
}
