using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
using PlayniteDisplayManager.Refresh;

namespace PlayniteDisplayManager.Profiles
{
    public sealed class GameDisplayProfileStore
    {
        private readonly string filePath;
        private readonly object syncRoot = new object();
        private Dictionary<Guid, GameDisplayProfile> profiles = new Dictionary<Guid, GameDisplayProfile>();

        public GameDisplayProfileStore(string pluginDataPath)
        {
            Directory.CreateDirectory(pluginDataPath);
            filePath = Path.Combine(pluginDataPath, "gameProfiles.json");
            Load();
        }

        public GameDisplayProfile GetProfile(Game game)
        {
            if (game == null)
            {
                return null;
            }

            return GetProfile(game.Id);
        }

        public GameDisplayProfile GetProfile(Guid gameId)
        {
            lock (syncRoot)
            {
                return profiles.TryGetValue(gameId, out var profile) ? profile.Clone() : null;
            }
        }

        public GameHdrOverride GetHdrOverride(Game game)
        {
            return GetProfile(game)?.HdrOverride ?? GameHdrOverride.Inherit;
        }

        public GameRefreshRateOverride GetRefreshRateOverride(Game game)
        {
            return GetProfile(game)?.RefreshRateOverride ?? GameRefreshRateOverride.Inherit;
        }

        public int CountNonInherit()
        {
            lock (syncRoot)
            {
                return profiles.Values.Count(p => p != null && !p.IsEmpty);
            }
        }

        public Dictionary<Guid, GameDisplayProfile> GetProfilesSnapshot()
        {
            lock (syncRoot)
            {
                return profiles.ToDictionary(a => a.Key, a => a.Value.Clone());
            }
        }

        public void SetHdrOverride(Game game, GameHdrOverride hdrOverride)
        {
            if (game == null)
            {
                return;
            }

            lock (syncRoot)
            {
                var profile = GetOrCreateUnlocked(game.Id);
                profile.HdrOverride = hdrOverride;
                PersistUnlocked(game.Id, profile);
            }
        }

        public void SetRefreshRateOverride(Game game, GameRefreshRateOverride refreshOverride)
        {
            if (game == null)
            {
                return;
            }

            lock (syncRoot)
            {
                var profile = GetOrCreateUnlocked(game.Id);
                profile.RefreshRateOverride = refreshOverride;
                PersistUnlocked(game.Id, profile);
            }
        }

        public void ClearProfile(Game game)
        {
            if (game == null)
            {
                return;
            }

            lock (syncRoot)
            {
                profiles.Remove(game.Id);
                Save();
            }
        }

        private GameDisplayProfile GetOrCreateUnlocked(Guid gameId)
        {
            if (profiles.TryGetValue(gameId, out var existing) && existing != null)
            {
                return existing.Clone();
            }

            return new GameDisplayProfile();
        }

        private void PersistUnlocked(Guid gameId, GameDisplayProfile profile)
        {
            if (profile == null || profile.IsEmpty)
            {
                profiles.Remove(gameId);
            }
            else
            {
                profiles[gameId] = profile;
            }

            Save();
        }

        private void Load()
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            if (Serialization.TryFromJsonFile<Dictionary<Guid, GameDisplayProfile>>(filePath, out var loaded))
            {
                profiles = loaded ?? new Dictionary<Guid, GameDisplayProfile>();
            }
        }

        private void Save()
        {
            File.WriteAllText(filePath, Serialization.ToJson(profiles, true));
        }
    }
}
