// AILogger.cs
// Per-faction AI decision logging to files in /logs folder.
// Each AI faction gets its own file. Logs are cleared on game start.
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace TheWaningBorder.AI
{
    /// <summary>
    /// Writes AI decisions to per-faction log files for debugging.
    /// Files live in {ProjectRoot}/logs/AI_{FactionName}.log
    /// </summary>
    public static class AILogger
    {
        private static string _logFolder;
        private static Dictionary<Faction, StreamWriter> _writers = new Dictionary<Faction, StreamWriter>();
        private static float _gameStartTime;
        private static bool _initialized;

        /// <summary>
        /// Call once at game start to clear old logs and prepare the folder.
        /// </summary>
        public static void Initialize()
        {
            Cleanup(); // Close any previous writers

            _logFolder = Path.Combine(Application.dataPath, "..", "logs");

            // Clear and recreate folder
            if (Directory.Exists(_logFolder))
            {
                foreach (var file in Directory.GetFiles(_logFolder, "AI_*.log"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
            else
            {
                Directory.CreateDirectory(_logFolder);
            }

            _gameStartTime = Time.time;
            _writers.Clear();
            _initialized = true;

            Debug.Log($"[AILogger] Logs folder: {Path.GetFullPath(_logFolder)}");
        }

        /// <summary>
        /// Log a decision for a specific faction.
        /// </summary>
        /// <param name="faction">Which AI faction</param>
        /// <param name="category">System name: ECONOMY, BUILDING, MILITARY, SCOUT, etc.</param>
        /// <param name="message">What happened</param>
        public static void Log(Faction faction, string category, string message)
        {
            if (!_initialized) return;

            var writer = GetWriter(faction);
            if (writer == null) return;

            float elapsed = Time.time - _gameStartTime;
            int minutes = (int)(elapsed / 60f);
            float seconds = elapsed % 60f;

            writer.WriteLine($"[{minutes:D2}:{seconds:00.0}] {category}: {message}");
            writer.Flush();
        }

        /// <summary>
        /// Close all file handles. Call when returning to main menu.
        /// </summary>
        public static void Cleanup()
        {
            foreach (var kvp in _writers)
            {
                try { kvp.Value?.Close(); } catch { }
            }
            _writers.Clear();
            _initialized = false;
        }

        private static StreamWriter GetWriter(Faction faction)
        {
            if (_writers.TryGetValue(faction, out var existing))
                return existing;

            try
            {
                string path = Path.Combine(_logFolder, $"AI_{faction}.log");
                var writer = new StreamWriter(path, append: false);
                writer.AutoFlush = false;
                writer.WriteLine($"=== AI Log for {faction} — Game started ===");
                writer.WriteLine();
                _writers[faction] = writer;
                return writer;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[AILogger] Failed to create log for {faction}: {e.Message}");
                return null;
            }
        }
    }
}
