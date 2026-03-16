using System.ComponentModel;

namespace WorldBuilder.Shared.Lib.Settings {
    /// <summary>
    /// The type of DAT source for a project.
    /// </summary>
    public enum DatSourceType {
        /// <summary>
        /// Use a managed DAT set.
        /// </summary>
        [Description("Managed")]
        Managed,
        /// <summary>
        /// Use a local DAT directory.
        /// </summary>
        [Description("Add New")]
        AddNew
    }

    /// <summary>
    /// The type of ACE source for a project.
    /// </summary>
    public enum AceSourceType {
        /// <summary>
        /// Do not use an ACE database.
        /// </summary>
        [Description("None")]
        None,
        /// <summary>
        /// Use a managed ACE database.
        /// </summary>
        [Description("Managed")]
        Managed,
        /// <summary>
        /// Use a local ACE database file.
        /// </summary>
        [Description("Add New")]
        Local
    }
}
