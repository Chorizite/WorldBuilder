using CommunityToolkit.Mvvm.ComponentModel;
using System;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.ViewModels {
    public partial class RecentProject : ObservableObject {
        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private DateTime _lastOpened = DateTime.Now;

        [ObservableProperty]
        private Guid _guid = Guid.NewGuid();

        [ObservableProperty]
        private string _path = string.Empty;

        [ObservableProperty]
        private string _remoteUrl = string.Empty;

        [ObservableProperty]
        private bool _isRemote = false;

        public RecentProject() { }

        public RecentProject(Project project) {
            Name = project.Name;
            LastOpened = DateTime.Now;
            Guid = project.Guid;
            Path = project.FilePath;
        }
    }
}
