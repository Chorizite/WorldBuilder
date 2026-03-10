namespace WorldBuilder.Lib {
    /// <summary>
    /// Global command-line options for the application.
    /// </summary>
    public class CommandLineOptions {
        /// <summary>
        /// Gets whether the legacy rendering pipeline is forced to be used.
        /// </summary>
        public bool ForceLegacyRendering { get; set; }

        /// <summary>
        /// Gets the path to a project file to open on startup.
        /// </summary>
        public string? ProjectPath { get; set; }

        /// <summary>
        /// Parses command-line arguments into a CommandLineOptions instance.
        /// </summary>
        /// <param name="args">The command-line arguments</param>
        /// <returns>A new CommandLineOptions instance</returns>
        public static CommandLineOptions Parse(string[] args) {
            var options = new CommandLineOptions();
            if (args == null) return options;

            for (int i = 1; i < args.Length; i++) {
                var arg = args[i];

                if (arg.Equals("--force-legacy-rendering", System.StringComparison.OrdinalIgnoreCase)) {
                    options.ForceLegacyRendering = true;
                }
                else if (arg.Equals("--project", System.StringComparison.OrdinalIgnoreCase) || arg.Equals("-p", System.StringComparison.OrdinalIgnoreCase)) {
                    if (i + 1 < args.Length) {
                        options.ProjectPath = args[++i];
                    }
                }
                else if (!arg.StartsWith("-") && string.IsNullOrEmpty(options.ProjectPath)) {
                    options.ProjectPath = arg;
                }
            }
            return options;
        }
    }
}