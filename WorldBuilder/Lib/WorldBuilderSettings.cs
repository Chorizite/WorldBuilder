using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Lib {
    public partial class WorldBuilderSettings : ObservableObject {
        private readonly static JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions() { WriteIndented = true };
        private readonly static string _savePath = "worldbuilder.json";

        [ObservableProperty]
        private string _dataPath = Path.GetFullPath("data");

        [ObservableProperty]
        private ObservableCollection<RecentProject> _recentProjects = new ObservableCollection<RecentProject>();

        public static WorldBuilderSettings FromFile() {
            if (!File.Exists(_savePath)) {
                return new WorldBuilderSettings();
            }
            return JsonSerializer.Deserialize<WorldBuilderSettings>(File.ReadAllText(_savePath), _jsonSerializerOptions) ?? new();
        }

        public WorldBuilderSettings() {
            PropertyChanged += WorldBuilderSettings_PropertyChanged;
        }

        internal void AddRecentProject(Project value) {
            if (RecentProjects.Any(x => x.Path == value.FilePath)) {
                RecentProjects.Remove(RecentProjects.First(x => x.Path == value.FilePath));
            }
            RecentProjects.Insert(0, new RecentProject(value));

            OnPropertyChanged(nameof(RecentProjects));
        }

        private void WorldBuilderSettings_PropertyChanged(object? sender, PropertyChangedEventArgs e) {
            File.WriteAllText(_savePath, JsonSerializer.Serialize(this, _jsonSerializerOptions));
        }
    }
}
