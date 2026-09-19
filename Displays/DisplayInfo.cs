using System;

namespace PlayniteDisplayManager.Displays
{
    public sealed class DisplayInfo
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public string CustomName { get; set; }

        public string EffectiveName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(CustomName))
                {
                    return CustomName.Trim();
                }

                return string.IsNullOrWhiteSpace(Name) ? "Display" : Name.Trim();
            }
        }

        public DisplayConnectionState State { get; set; }

        public bool IsConnected { get; set; }

        public bool IsPrimary { get; set; }

        public bool IsVisible { get; set; } = true;

        public string GdiDeviceName { get; set; }

        public string AdapterPath { get; set; }

        public uint ConnectorInstance { get; set; }

        public string MonitorDevicePath { get; set; }

        public string IdentityKind { get; set; }

        public ushort? EdidManufactureId { get; set; }

        public ushort? EdidProductCodeId { get; set; }

        public uint? EdidSerial { get; set; }

        public uint Width { get; set; }

        public uint Height { get; set; }

        public double RefreshRateHz { get; set; }

        public DisplayInfo Clone()
        {
            return new DisplayInfo
            {
                Id = Id,
                Name = Name,
                CustomName = CustomName,
                State = State,
                IsConnected = IsConnected,
                IsPrimary = IsPrimary,
                IsVisible = IsVisible,
                GdiDeviceName = GdiDeviceName,
                AdapterPath = AdapterPath,
                ConnectorInstance = ConnectorInstance,
                MonitorDevicePath = MonitorDevicePath,
                IdentityKind = IdentityKind,
                EdidManufactureId = EdidManufactureId,
                EdidProductCodeId = EdidProductCodeId,
                EdidSerial = EdidSerial,
                Width = Width,
                Height = Height,
                RefreshRateHz = RefreshRateHz
            };
        }
    }
}
