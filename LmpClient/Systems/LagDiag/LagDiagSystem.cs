using LmpClient.Base;
using LmpClient.Systems.TimeSync;
using LmpCommon.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LmpClient.Systems.LagDiag
{
    /// <summary>
    /// Diagnostic system that tracks per-system drain counts from the 10 Vessel
    /// systems whose Process*() methods pull items from their queues.
    /// Strategy: hybrid - ring buffer (10000) for precise post-mortem,
    ///                    1-second aggregated summary to LunaLog,
    ///                    on-demand CSV dump from DebugWindow.
    /// </summary>
    public class LagDiagSystem : System<LagDiagSystem>
    {
        #region Constructor

        /// <summary>
        /// Routine MUST be set up here (not in OnEnabled) because AlwaysEnabled=true
        /// means OnEnabled() is never called by the System base class.
        /// Follows PingSystem.cs:19 pattern.
        /// </summary>
        public LagDiagSystem()
        {
            SetupRoutine(new RoutineDefinition(1000, RoutineExecution.Update, FlushAggregatedLog));
        }

        #endregion

        #region Base overrides

        protected override bool AlwaysEnabled => true;
        public override string SystemName { get; } = nameof(LagDiagSystem);

        /// <summary>
        /// Reset stats whenever the client disconnects so data does not pollute
        /// across sessions.
        /// </summary>
        protected override void NetworkEventHandler(ClientState data)
        {
            base.NetworkEventHandler(data);
            if (data <= ClientState.Disconnected)
            {
                ResetStats();
            }
        }

        #endregion

        #region Data types

        public class DrainStats
        {
            public int LastDrainCount;
            public int MaxDrainCount;
            public long TotalDrainCount;
            public long LastElapsedMs;
            public long MaxElapsedMs;
            public long TotalElapsedMs;
            public long SampleCount;
        }

        private class DrainRecord
        {
            public double GameTime;
            public string SystemName;
            public int Count;
            public long ElapsedMs;
        }

        #endregion

        #region Fields

        private const int MaxRingBufferSize = 10000;

        private readonly Dictionary<string, DrainStats> _stats = new Dictionary<string, DrainStats>();
        private readonly Queue<DrainRecord> _ringBuffer = new Queue<DrainRecord>();
        private readonly object _lock = new object();
        private readonly StringBuilder _flushBuilder = new StringBuilder();

        #endregion

        #region Public API

        /// <summary>
        /// Called by each Vessel*System.Process*() method AFTER it has drained its
        /// queue for one tick. Thread-safe. Intended to be called on Unity thread.
        /// </summary>
        public void ReportDrain(string systemName, int count, long elapsedMs)
        {
            if (count == 0 && elapsedMs == 0) return;

            lock (_lock)
            {
                if (!_stats.TryGetValue(systemName, out var s))
                {
                    s = new DrainStats();
                    _stats[systemName] = s;
                }

                s.LastDrainCount = count;
                if (count > s.MaxDrainCount) s.MaxDrainCount = count;
                s.TotalDrainCount += count;

                s.LastElapsedMs = elapsedMs;
                if (elapsedMs > s.MaxElapsedMs) s.MaxElapsedMs = elapsedMs;
                s.TotalElapsedMs += elapsedMs;
                s.SampleCount++;

                _ringBuffer.Enqueue(new DrainRecord
                {
                    GameTime = TimeSyncSystem.UniversalTime,
                    SystemName = systemName,
                    Count = count,
                    ElapsedMs = elapsedMs
                });
                while (_ringBuffer.Count > MaxRingBufferSize)
                {
                    _ringBuffer.Dequeue();
                }
            }
        }

        /// <summary>
        /// Snapshot copy for DebugWindow display. Safe to iterate outside the lock.
        /// </summary>
        public Dictionary<string, DrainStats> GetSnapshot()
        {
            lock (_lock)
            {
                var copy = new Dictionary<string, DrainStats>(_stats.Count);
                foreach (var kv in _stats)
                {
                    copy[kv.Key] = new DrainStats
                    {
                        LastDrainCount = kv.Value.LastDrainCount,
                        MaxDrainCount = kv.Value.MaxDrainCount,
                        TotalDrainCount = kv.Value.TotalDrainCount,
                        LastElapsedMs = kv.Value.LastElapsedMs,
                        MaxElapsedMs = kv.Value.MaxElapsedMs,
                        TotalElapsedMs = kv.Value.TotalElapsedMs,
                        SampleCount = kv.Value.SampleCount,
                    };
                }
                return copy;
            }
        }

        /// <summary>
        /// Writes the ring buffer to a CSV file under KSP/Logs/.
        /// Returns the file path or null on failure.
        /// </summary>
        public string DumpRingBufferToFile()
        {
            DrainRecord[] snapshot;
            lock (_lock)
            {
                snapshot = _ringBuffer.ToArray();
            }

            try
            {
                var logsDir = Path.Combine(MainSystem.KspPath, "Logs");
                if (!Directory.Exists(logsDir))
                {
                    Directory.CreateDirectory(logsDir);
                }

                var fileName = "lmp_lagdiag_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".csv";
                var fullPath = Path.Combine(logsDir, fileName);

                var sb = new StringBuilder(snapshot.Length * 64);
                sb.AppendLine("GameTime,SystemName,DrainCount,ElapsedMs");
                for (var i = 0; i < snapshot.Length; i++)
                {
                    var r = snapshot[i];
                    sb.Append(r.GameTime.ToString("F3"));
                    sb.Append(',');
                    sb.Append(r.SystemName);
                    sb.Append(',');
                    sb.Append(r.Count);
                    sb.Append(',');
                    sb.Append(r.ElapsedMs);
                    sb.Append('\n');
                }

                File.WriteAllText(fullPath, sb.ToString());
                LunaLog.Log("[LagDiag] Dumped " + snapshot.Length + " records to " + fullPath);
                return fullPath;
            }
            catch (Exception ex)
            {
                LunaLog.LogError("[LagDiag] Dump failed: " + ex.Message);
                return null;
            }
        }

        public void ResetStats()
        {
            lock (_lock)
            {
                _stats.Clear();
                _ringBuffer.Clear();
            }
        }

        #endregion

        #region Routines

        /// <summary>
        /// Runs every 1 second. Emits one aggregated log line with last-second stats
        /// for all systems that had activity. Keeps log rate at ~1/sec regardless of
        /// how many drains happened (prevents the 196 log/sec performance cliff).
        /// </summary>
        private void FlushAggregatedLog()
        {
            if (MainSystem.NetworkState < ClientState.Running) return;

            Dictionary<string, DrainStats> snap;
            lock (_lock)
            {
                if (_stats.Count == 0) return;
                snap = new Dictionary<string, DrainStats>(_stats.Count);
                foreach (var kv in _stats)
                {
                    snap[kv.Key] = new DrainStats
                    {
                        LastDrainCount = kv.Value.LastDrainCount,
                        MaxDrainCount = kv.Value.MaxDrainCount,
                        LastElapsedMs = kv.Value.LastElapsedMs,
                        MaxElapsedMs = kv.Value.MaxElapsedMs,
                    };
                }
            }

            _flushBuilder.Length = 0;
            _flushBuilder.Append("[LagDiag] UT=");
            _flushBuilder.Append(TimeSyncSystem.UniversalTime.ToString("F1"));
            foreach (var kv in snap)
            {
                _flushBuilder.Append(' ');
                _flushBuilder.Append(kv.Key);
                _flushBuilder.Append(":last=");
                _flushBuilder.Append(kv.Value.LastDrainCount);
                _flushBuilder.Append("/ms=");
                _flushBuilder.Append(kv.Value.LastElapsedMs);
                _flushBuilder.Append("/max=");
                _flushBuilder.Append(kv.Value.MaxDrainCount);
            }

            LunaLog.Log(_flushBuilder.ToString());
        }

        #endregion
    }
}
