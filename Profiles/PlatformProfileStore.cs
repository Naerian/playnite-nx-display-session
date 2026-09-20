using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Playnite.SDK.Data;

namespace PlayniteDisplayManager.Profiles
{
    public sealed class PlatformProfileStore
    {
        private readonly string filePath;
        private readonly object syncRoot = new object();
        private Dictionary<Guid, GameDisplayProfile> profiles = new Dictionary<Guid, GameDisplayProfile>();

        public PlatformProfileStore(string pluginDataPath)
        {
            Directory.CreateDirectory(pluginDataPath);
            filePath = Path.Combine(pluginDataPath, "platformProfiles.json");
            Load();
        }

        public GameDisplayProfile GetProfile(Guid platformId)
        {
            if (platformId == Guid.Empty)
            {
                return null;
            }

            lock (syncRoot)
            {
                return profiles.TryGetValue(platformId, out var profile) ? profile.Clone() : null;
            }
        }

        public Dictionary<Guid, GameDisplayProfile> GetProfilesSnapshot()
        {
            lock (syncRoot)
            {
                return profiles.ToDictionary(a => a.Key, a => a.Value.Clone());
            }
        }

        public void SetProfile(Guid platformId, GameDisplayProfile profile)
        {
            if (platformId == Guid.Empty)
            {
                return;
            }

            lock (syncRoot)
            {
                if (profile == null || profile.IsEmpty)
                {
                    profiles.Remove(platformId);
                }
                else
                {
                    profiles[platformId] = profile.Clone();
                }

                Save();
            }
        }

        public void ClearProfile(Guid platformId)
        {
            if (platformId == Guid.Empty)
            {
                return;
            }

            lock (syncRoot)
            {
                profiles.Remove(platformId);
                Save();
            }
        }

        public void ReplaceProfiles(IEnumerable<KeyValuePair<Guid, GameDisplayProfile>> entries)
        {
            lock (syncRoot)
            {
                profiles = (entries ?? Enumerable.Empty<KeyValuePair<Guid, GameDisplayProfile>>())
                    .Where(e => e.Key != Guid.Empty && e.Value != null && !e.Value.IsEmpty)
                    .ToDictionary(e => e.Key, e => e.Value.Clone());
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
