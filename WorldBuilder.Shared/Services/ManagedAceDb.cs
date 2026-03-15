using System;

namespace WorldBuilder.Shared.Services {
    /// <summary>
    /// Information about a managed ACE SQLite database.
    /// </summary>
    public class ManagedAceDb {
        public Guid Id { get; set; }
        public string FriendlyName { get; set; } = string.Empty;
        public string BaseVersion { get; set; } = string.Empty;
        public string PatchVersion { get; set; } = string.Empty;
        public string LastModified { get; set; } = string.Empty;
        public string Md5 { get; set; } = string.Empty;
        public DateTime ImportDate { get; set; }

        public string DisplayVersion => $"{BaseVersion} - {Md5.Substring(0, 8)})";
    }
}
