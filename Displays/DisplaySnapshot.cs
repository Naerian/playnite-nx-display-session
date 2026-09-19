using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace PlayniteDisplayManager.Displays
{
    [DataContract]
    public sealed class DisplaySnapshot
    {
        public const string CurrentSchema = "1";

        [DataMember(Name = "schema")]
        public string Schema { get; set; } = CurrentSchema;

        [DataMember(Name = "capturedUtc")]
        public string CapturedUtc { get; set; }

        [DataMember(Name = "queryFlags")]
        public uint QueryFlags { get; set; }

        [DataMember(Name = "pathCount")]
        public int PathCount { get; set; }

        [DataMember(Name = "modeCount")]
        public int ModeCount { get; set; }

        [DataMember(Name = "pathsBase64")]
        public string PathsBase64 { get; set; }

        [DataMember(Name = "modesBase64")]
        public string ModesBase64 { get; set; }

        [DataMember(Name = "displays")]
        public List<DisplaySnapshotEntry> Displays { get; set; } = new List<DisplaySnapshotEntry>();

        public static string ToJson(DisplaySnapshot snapshot)
        {
            var serializer = new DataContractJsonSerializer(typeof(DisplaySnapshot));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, snapshot);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static DisplaySnapshot FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var serializer = new DataContractJsonSerializer(typeof(DisplaySnapshot));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return serializer.ReadObject(stream) as DisplaySnapshot;
            }
        }

        public void SaveToFile(string path)
        {
            File.WriteAllText(path, ToJson(this), new UTF8Encoding(false));
        }

        public static DisplaySnapshot LoadFromFile(string path)
        {
            return FromJson(File.ReadAllText(path, Encoding.UTF8));
        }
    }

    [DataContract]
    public sealed class DisplaySnapshotEntry
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "isPrimary")]
        public bool IsPrimary { get; set; }

        [DataMember(Name = "gdi")]
        public string GdiDeviceName { get; set; }

        [DataMember(Name = "width")]
        public uint Width { get; set; }

        [DataMember(Name = "height")]
        public uint Height { get; set; }
    }
}
