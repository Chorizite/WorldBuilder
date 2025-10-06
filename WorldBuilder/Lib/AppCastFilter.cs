using NetSparkleUpdater;
using NetSparkleUpdater.AppCastHandlers;
using NetSparkleUpdater.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder.Lib {
    public class OSAppCastFilter : IAppCastFilter {
        private ILogger? _logWriter;

        public OSAppCastFilter(ILogger? logWriter = null) {
            _logWriter = logWriter;
        }
        public string OSName => "windows-x64";

        public string Channel => App.Version.Contains("+") ? "edge" : "stable";

        /// <inheritdoc/>
        public IEnumerable<AppCastItem> GetFilteredAppCastItems(SemVerLike installed, IEnumerable<AppCastItem> items) {
            return items.Where((item) => {
                var semVer = SemVerLike.Parse(item.Version);
                var appCastItemChannel = item.Channel ?? "";

                // Filter out other channels
                if (Channel != appCastItemChannel) {
                    return false;
                }

                // Filter out older versions
                if (semVer.CompareTo(installed) <= 0) {
                    return false;
                }

                return item.OperatingSystem?.ToLower() == OSName.ToLower();
            }).OrderByDescending(x => x.SemVerLikeVersion);
        }
    }
}
