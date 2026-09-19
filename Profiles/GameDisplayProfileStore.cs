using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playnite.SDK.Data;
using Playnite.SDK.Models;

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
                if (hdrOverride == GameHdrOverride.Inherit)
                {
                    profiles.Remove(game.Id);
                }
                else
                {
                    profiles[game.Id] = new GameDisplayProfile
                    {
                        HdrOverride = hdrOverride
                    };
                }

                Save();
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
