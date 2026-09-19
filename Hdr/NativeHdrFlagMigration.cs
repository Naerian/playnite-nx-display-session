using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;

namespace PlayniteDisplayManager.Hdr
{
    [DataContract]
    public sealed class NativeHdrFlagBackup
    {
        [DataMember(Name = "clearedUtc")]
        public string ClearedUtc { get; set; }

        [DataMember(Name = "lastClearedCount")]
        public int LastClearedCount { get; set; }

        [DataMember(Name = "gameIds")]
        public List<Guid> GameIds { get; set; } = new List<Guid>();
    }

    public sealed class NativeHdrMigrationResult
    {
        public int ClearedCount { get; set; }
        public int RemainingEnabledCount { get; set; }
        public int BackupIdCount { get; set; }
        public string Error { get; set; }
        public bool Success => string.IsNullOrWhiteSpace(Error);
    }

    /// <summary>
    /// Clears Playnite's native EnableSystemHdr so Display Manager does not stack two restores.
    /// Backup is stored in plugin user data for Maintenance reverse.
    /// </summary>
    public sealed class NativeHdrFlagMigration
    {
        private readonly IPlayniteAPI api;
        private readonly ILogger logger;
        private readonly string backupPath;
        private readonly object syncRoot = new object();

        public NativeHdrFlagMigration(IPlayniteAPI playniteApi, ILogger log, string pluginDataPath)
        {
            api = playniteApi ?? throw new ArgumentNullException(nameof(playniteApi));
            logger = log;
            Directory.CreateDirectory(pluginDataPath);
            backupPath = Path.Combine(pluginDataPath, "nativeHdrFlagBackup.json");
        }

        public int CountEnabled()
        {
            try
            {
                return api.Database?.Games?.Count(g => g != null && g.EnableSystemHdr) ?? 0;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "Failed to count EnableSystemHdr flags.");
                return 0;
            }
        }

        public int CountBackupIds()
        {
            return LoadBackup()?.GameIds?.Count ?? 0;
        }

        public NativeHdrMigrationResult ClearAllEnabled()
        {
            var result = new NativeHdrMigrationResult();
            try
            {
                var games = api.Database?.Games;
                if (games == null)
                {
                    result.Error = "Game database is not available.";
                    return result;
                }

                var toClear = games.Where(g => g != null && g.EnableSystemHdr).ToList();
                if (toClear.Count == 0)
                {
                    result.RemainingEnabledCount = 0;
                    result.BackupIdCount = CountBackupIds();
                    return result;
                }

                MergeBackup(toClear.Select(g => g.Id).ToList(), toClear.Count);

                using (api.Database.BufferedUpdate())
                {
                    foreach (var game in toClear)
                    {
                        game.EnableSystemHdr = false;
                    }

                    games.Update(toClear);
                }

                result.ClearedCount = toClear.Count;
                result.RemainingEnabledCount = CountEnabled();
                result.BackupIdCount = CountBackupIds();
                logger?.Info("Cleared EnableSystemHdr on " + result.ClearedCount + " game(s).");
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "Failed to clear EnableSystemHdr flags.");
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Clears the native flag on one game (NX wins). Returns true when a change was written.
        /// </summary>
        public bool TryClearGame(Game game)
        {
            if (game == null || !game.EnableSystemHdr)
            {
                return false;
            }

            try
            {
                MergeBackup(new List<Guid> { game.Id }, 1);
                game.EnableSystemHdr = false;
                api.Database.Games.Update(game);
                logger?.Info("Cleared native EnableSystemHdr on " + game.Name + " (Display Manager owns HDR).");
                return true;
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "Failed to clear EnableSystemHdr on " + game?.Name);
                return false;
            }
        }

        public NativeHdrMigrationResult RestoreFromBackup()
        {
            var result = new NativeHdrMigrationResult();
            try
            {
                var backup = LoadBackup();
                var ids = backup?.GameIds;
                if (ids == null || ids.Count == 0)
                {
                    result.Error = "No native HDR flag backup was found.";
                    return result;
                }

                var games = api.Database?.Games;
                if (games == null)
                {
                    result.Error = "Game database is not available.";
                    return result;
                }

                var toRestore = new List<Game>();
                foreach (var id in ids.Distinct())
                {
                    var game = games.Get(id);
                    if (game == null)
                    {
                        continue;
                    }

                    if (!game.EnableSystemHdr)
                    {
                        game.EnableSystemHdr = true;
                        toRestore.Add(game);
                    }
                }

                if (toRestore.Count > 0)
                {
                    using (api.Database.BufferedUpdate())
                    {
                        games.Update(toRestore);
                    }
                }

                result.ClearedCount = toRestore.Count; // restored count reused field
                result.RemainingEnabledCount = CountEnabled();
                result.BackupIdCount = ids.Count;
                logger?.Info("Restored EnableSystemHdr on " + toRestore.Count + " game(s) from backup.");
            }
            catch (Exception ex)
            {
                logger?.Error(ex, "Failed to restore EnableSystemHdr from backup.");
                result.Error = ex.Message;
            }

            return result;
        }

        private void MergeBackup(IList<Guid> newIds, int lastClearedCount)
        {
            lock (syncRoot)
            {
                var backup = LoadBackup() ?? new NativeHdrFlagBackup();
                var set = new HashSet<Guid>(backup.GameIds ?? new List<Guid>());
                foreach (var id in newIds ?? Array.Empty<Guid>())
                {
                    set.Add(id);
                }

                backup.GameIds = set.ToList();
                backup.LastClearedCount = lastClearedCount;
                backup.ClearedUtc = DateTime.UtcNow.ToString("o");
                File.WriteAllText(backupPath, Serialization.ToJson(backup, true));
            }
        }

        private NativeHdrFlagBackup LoadBackup()
        {
            lock (syncRoot)
            {
                if (!File.Exists(backupPath))
                {
                    return null;
                }

                if (Serialization.TryFromJsonFile<NativeHdrFlagBackup>(backupPath, out var loaded))
                {
                    return loaded;
                }

                return null;
            }
        }
    }
}
