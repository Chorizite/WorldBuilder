using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Factories;
using Avalonia;
using WorldBuilder.Lib;
using System.IO;
using Avalonia.Platform.Storage;
using WorldBuilder.Shared.Models;
using CommunityToolkit.Mvvm.Messaging;
using WorldBuilder.Messages;

namespace WorldBuilder.ViewModels.Pages {
    public partial class GettingStartedPageViewModel : PageViewModel {
        public override string WindowName => "Getting Started";

        [ObservableProperty]
        private ObservableCollection<RecentProject> _recentProjects = new ObservableCollection<RecentProject>();
        private readonly WorldBuilderSettings _settings;

        public GettingStartedPageViewModel(WorldBuilderSettings settings) {
            _settings = settings;
            LoadRecentProjects();
        }

        private void LoadRecentProjects() {
            foreach (var project in _settings.RecentProjects) {
                RecentProjects.Add(project);
            }
        }

        [RelayCommand]
        private void GotoNewLocalProject() {
            ParentWindow?.NavigateToPage(PageName.NewLocalProject);
        }

        [RelayCommand]
        private async Task OpenExistingLocalProject() {
            var topLevel = GetTopLevel();

            if (topLevel == null || topLevel.StorageProvider.CanPickFolder == false) return;

            var res = await topLevel.StorageProvider.OpenFilePickerAsync(new() {
                AllowMultiple = false,
                Title = "Open Existing Project",
                SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(new Uri(Path.Combine(_settings.DataPath, "projects"))),
                FileTypeFilter = new List<FilePickerFileType> {
                    new FilePickerFileType("WorldBuilder Project") { Patterns = new[] { "*.wbproj" } }
                }
            });

            if (res?.Count() == 1) {
                Console.WriteLine($"Opening project: {res.First().Path.AbsolutePath}");
                var project = Project.FromDisk(res.First().Path.AbsolutePath);
                if (project is not null) {
                    WeakReferenceMessenger.Default.Send(new OpenProjectMessage(project));
                }
            }
        }

        [RelayCommand]
        private void OpenRecentProject(RecentProject recentProject) {
            if (recentProject == null) return;

            try {
                var project = Project.FromDisk(recentProject.Path);
                if (project is not null) {
                    WeakReferenceMessenger.Default.Send(new OpenProjectMessage(project));
                }
            }
            catch (Exception ex) {
                Console.WriteLine($"Error opening project: {ex.Message}");
            }
        }
    }

    public class GettingStartedPageViewModelDesign : GettingStartedPageViewModel {
        public GettingStartedPageViewModelDesign() : base(new()) {
            RecentProjects.Add(new RecentProject { Name = "Test Project", LastOpened = DateTime.Now, Path = "C:\\Projects\\TestProject" });
            RecentProjects.Add(new RecentProject { Name = "Test Project 2", LastOpened = DateTime.Now, Path = "C:\\Projects\\TestProject2" });
            RecentProjects.Add(new RecentProject { Name = "Test Project 3", LastOpened = DateTime.Now, Path = "C:\\Projects\\TestProject3", RemoteUrl = "https://localhost:5000/Test Project 3/", IsRemote = true });

        }
    }
}
