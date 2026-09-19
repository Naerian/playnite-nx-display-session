using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Playnite.SDK;
using Playnite.SDK.Plugins;

namespace PlayniteDisplayManager.Audio
{
    public sealed class AudioDeviceOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// Soft bridge to Audio Switcher (Guid 708b6ec4-…). No hard assembly reference —
    /// if the plugin is missing, all calls no-op / return empty.
    /// </summary>
    public sealed class AudioSwitcherBridge
    {
        public static readonly Guid PluginId = Guid.Parse("708b6ec4-bf96-4c0d-bd9d-fe0aa04d6bf1");

        private readonly IPlayniteAPI api;
        private readonly ILogger logger;

        public AudioSwitcherBridge(IPlayniteAPI playniteApi, ILogger log)
        {
            api = playniteApi;
            logger = log;
        }

        public bool IsAvailable
        {
            get
            {
                try
                {
                    return FindPlugin() != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        public string GetStatusLabel(Func<string, string> loc)
        {
            return IsAvailable
                ? loc("LOCDisplayManager_AudioSwitcherInstalled")
                : loc("LOCDisplayManager_AudioSwitcherMissing");
        }

        public IReadOnlyList<AudioDeviceOption> GetPlaybackDevices()
        {
            var results = new List<AudioDeviceOption>();
            try
            {
                var plugin = FindPlugin();
                if (plugin == null)
                {
                    return results;
                }

                var method = plugin.GetType().GetMethod(
                    "GetThemeSelectorDevices",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    Type.EmptyTypes,
                    null);
                if (method == null)
                {
                    return results;
                }

                if (!(method.Invoke(plugin, null) is IEnumerable devices))
                {
                    return results;
                }

                foreach (var device in devices)
                {
                    if (device == null)
                    {
                        continue;
                    }

                    var id = ReadStringProperty(device, "Id");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    var name = ReadStringProperty(device, "SettingsDisplayName")
                        ?? ReadStringProperty(device, "EffectiveName")
                        ?? ReadStringProperty(device, "Name")
                        ?? id;
                    results.Add(new AudioDeviceOption { Id = id, Name = name });
                }
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "Failed to list Audio Switcher playback devices.");
            }

            return results;
        }

        public string TryGetCurrentPlaybackDeviceId()
        {
            try
            {
                var plugin = FindPlugin();
                var method = plugin?.GetType().GetMethod(
                    "GetCurrentDeviceId",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    Type.EmptyTypes,
                    null);
                return method?.Invoke(plugin, null) as string;
            }
            catch (Exception ex)
            {
                logger?.Warn(ex, "Failed to read Audio Switcher current device.");
                return null;
            }
        }

        public bool TrySetPlaybackDevice(string deviceId, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                error = "Device id is empty.";
                return false;
            }

            try
            {
                var plugin = FindPlugin();
                if (plugin == null)
                {
                    error = "Audio Switcher is not installed or not loaded.";
                    return false;
                }

                var method = plugin.GetType().GetMethod(
                    "SetThemeSelectedDevice",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(string) },
                    null);
                if (method == null)
                {
                    error = "Audio Switcher SetThemeSelectedDevice is unavailable.";
                    return false;
                }

                method.Invoke(plugin, new object[] { deviceId });
                return true;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                logger?.Warn(ex, "Failed to set Audio Switcher playback device.");
                return false;
            }
        }

        private Plugin FindPlugin()
        {
            try
            {
                return api?.Addons?.Plugins?.FirstOrDefault(p => p != null && p.Id == PluginId);
            }
            catch
            {
                return null;
            }
        }

        private static string ReadStringProperty(object target, string name)
        {
            var prop = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            return prop?.GetValue(target) as string;
        }
    }
}
