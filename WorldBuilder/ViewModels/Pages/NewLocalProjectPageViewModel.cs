using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Factories;
using WorldBuilder.Lib;
using Avalonia;
using WorldBuilder.Shared.Models;
using CommunityToolkit.Mvvm.Messaging;
using WorldBuilder.Messages;
using Avalonia.Platform.Storage;

namespace WorldBuilder.ViewModels.Pages {
    public partial class NewLocalProjectPageViewModel : PageViewModel {
        public override string WindowName => "New Local Project";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FullLocation))]
        [NotifyCanExecuteChangedFor(nameof(CreateProjectCommand))]
        private string _name = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(FullLocation))]
        [NotifyCanExecuteChangedFor(nameof(CreateProjectCommand))]
        private string _location = string.Empty;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(CreateProjectCommand))]
        private string _baseDatDirectory = @"C:\Turbine\Asheron's Call\";

        public string FullLocation => Path.Combine(Location, Name);

        [ObservableProperty]
        private bool _projectExists;

        [ObservableProperty]
        private bool _nameIsValid;

        [ObservableProperty]
        private bool _baseDatDirectoryIsValid;

        [ObservableProperty]
        private bool _isValid;

        public NewLocalProjectPageViewModel(WorldBuilderSettings settings) {
            Location = Path.GetFullPath(Path.Combine(settings.DataPath, "Projects"));
        }

        [RelayCommand]
        private void GoBack() {
            ParentWindow?.NavigateToPage(PageName.GettingStarted);
        }

        [RelayCommand]
        private async Task SelectLocation() {
            var topLevel = TopLevel.GetTopLevel(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null);

            if (topLevel == null || topLevel.StorageProvider.CanPickFolder == false) return;

            var dir = string.IsNullOrWhiteSpace(BaseDatDirectory) ? Path.GetDirectoryName(GetType().Assembly.Location) : Location;
            var res = await topLevel.StorageProvider.OpenFolderPickerAsync(new() {
                AllowMultiple = false,
                Title = "Select Project Location",
                SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(new Uri(dir))
            });

            if (res.Count == 1) {
                Location = res.First().TryGetLocalPath() ?? "";
            }
        }

        [RelayCommand]
        private async Task SelectBaseDatDirectory() {
            var topLevel = TopLevel.GetTopLevel(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null);

            if (topLevel == null || topLevel.StorageProvider.CanPickFolder == false) return;
            var dir = string.IsNullOrWhiteSpace(BaseDatDirectory) ? Path.GetDirectoryName(GetType().Assembly.Location) : BaseDatDirectory;
            var res = await topLevel.StorageProvider.OpenFolderPickerAsync(new() {
                AllowMultiple = false,
                Title = "Select Base Dat Directory",
                SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(new Uri(dir))
            });

            if (res.Count == 1) {
                BaseDatDirectory = res.First().TryGetLocalPath() ?? "";
            }
        }

        [RelayCommand(CanExecute = nameof(CanCreateProject))]
        private Task CreateProject() {
            var project = Project.Create(Name, Path.Combine(Location, Name, $"{Name}.wbproj"), BaseDatDirectory);
            if (project == null) return Task.CompletedTask;
            WeakReferenceMessenger.Default.Send(new OpenProjectMessage(project));
            return Task.CompletedTask;
        }

        private bool CanCreateProject() {
            // TODO: this should happen not here probably...
            NameIsValid = !string.IsNullOrWhiteSpace(Name);
            ProjectExists = NameIsValid && Directory.Exists(Path.Combine(Location, Name));
            BaseDatDirectoryIsValid = !string.IsNullOrWhiteSpace(BaseDatDirectory) && Directory.Exists(BaseDatDirectory)
                && File.Exists(Path.Combine(BaseDatDirectory, $"client_cell_1.dat"))
                && File.Exists(Path.Combine(BaseDatDirectory, $"client_portal.dat"))
                && File.Exists(Path.Combine(BaseDatDirectory, $"client_highres.dat"))
                && File.Exists(Path.Combine(BaseDatDirectory, $"client_local_English.dat"));

            IsValid = NameIsValid && !ProjectExists && BaseDatDirectoryIsValid;

            return IsValid;
        }
    }

    public class NewLocalProjectPageViewModelDesign : NewLocalProjectPageViewModel {
        public NewLocalProjectPageViewModelDesign() : base(new()) {
            Location = Path.GetFullPath(Path.Combine((new WorldBuilderSettings()).DataPath, "Projects"));
        }
    }
}
