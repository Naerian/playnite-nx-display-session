namespace PlayniteDisplayManager.Displays
{
    public sealed class DisplayDeviceAlias
    {
        public string DisplayId { get; set; }

        public string CustomName { get; set; }

        /// <summary>
        /// Windows friendly name remembered so orphan (disconnected) aliases stay readable.
        /// </summary>
        public string LastKnownName { get; set; }

        public string Icon { get; set; }

        public bool? IsVisible { get; set; }
    }
}
