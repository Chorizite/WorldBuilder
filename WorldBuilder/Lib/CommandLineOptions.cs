namespace WorldBuilder.Lib {
    /// <summary>
    /// Global command-line options for the application.
    /// </summary>
    public class CommandLineOptions {
        /// <summary>
        /// Gets whether bindless texturing is forced to be disabled.
        /// </summary>
        public bool DisableBindless { get; set; }

        /// <summary>
        /// Parses command-line arguments into a CommandLineOptions instance.
        /// </summary>
        /// <param name="args">The command-line arguments</param>
        /// <returns>A new CommandLineOptions instance</returns>
        public static CommandLineOptions Parse(string[] args) {
            var options = new CommandLineOptions();
            if (args == null) return options;
            foreach (var arg in args) {
                if (arg.Equals("--disable-bindless", System.StringComparison.OrdinalIgnoreCase)) {
                    options.DisableBindless = true;
                }
            }
            return options;
        }
    }
}