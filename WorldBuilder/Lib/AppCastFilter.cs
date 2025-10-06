using NetSparkleUpdater;
using NetSparkleUpdater.AppCastHandlers;
using NetSparkleUpdater.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder.Lib {
    /// <summary>
    /// Basic <seealso cref="IAppCastFilter"/> implementation for filtering
    /// your app cast items based on a channel name (e.g. "beta"). Makes it
    /// easy to allow your users to be on a beta software track or similar.
    /// Note that a "stable" channel search string will not be interpreted as versions 
    /// like "1.0.0"; it will look for versions like "1.0.0-stable1" (aka search for
    /// the string "stable").
    /// Names are compared in a case-insensitive manner.
    /// </summary>
    public class OSAppCastFilter : IAppCastFilter {
        private ILogger? _logWriter;

        public OSAppCastFilter(ILogger? logWriter = null) {
            RemoveOlderItems = true;
            _logWriter = logWriter;
            OSName = "windows-x64";
        }

        public bool RemoveOlderItems { get; set; }

        public string OSName { get; set; }

        public bool IsEdgeChannel => App.Version.Contains("+");

        /// <inheritdoc/>
        public IEnumerable<AppCastItem> GetFilteredAppCastItems(SemVerLike installed, IEnumerable<AppCastItem> items) {
            return items.Where((item) => {
                var semVer = SemVerLike.Parse(item.Version);
                var appCastItemChannel = item.Channel ?? "";
                if (RemoveOlderItems && semVer.CompareTo(installed) <= 0) {
                    _logWriter?.PrintMessage("Removing older item from filtered app cast results");
                    return false;
                }


                // Ignore edge channels if we are not on an edge channel
                if (!IsEdgeChannel && appCastItemChannel.Contains("edge")) {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(OSName)) {
                    return true;
                }
                return item.OperatingSystem?.ToLower() == OSName.ToLower();
            }).OrderByDescending(x => x.SemVerLikeVersion);
        }
    }
}
