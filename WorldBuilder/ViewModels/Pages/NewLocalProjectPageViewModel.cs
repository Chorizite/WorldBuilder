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
        private string _location;

        public string FullLocation => Path.Combine(Location, Name);

        [ObservableProperty]
        private bool _projectExists;

        [ObservableProperty]
        private bool _nameIsValid;

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

            var res = await topLevel.StorageProvider.OpenFolderPickerAsync(new() {
                AllowMultiple = false,
                Title = "Select Project Location",
                SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(new Uri(Location))
            });

            if (res.Count == 1) {
                Location = res.First().Path.AbsolutePath;
            }
        }

        [RelayCommand(CanExecute = nameof(CanCreateProject))]
        private Task CreateProject() {
            var project = Project.Create(Name, Path.Combine(Location, Name, $"{Name}.wbproj"));
            WeakReferenceMessenger.Default.Send(new OpenProjectMessage(project));
            return Task.CompletedTask;
        }

        private bool CanCreateProject() {
            // TODO: this should happen here probably...
            NameIsValid = !string.IsNullOrWhiteSpace(Name);
            ProjectExists = NameIsValid && Directory.Exists(Path.Combine(Location, Name));

            IsValid = NameIsValid && !ProjectExists;

            return IsValid;
        }
    }

    public class NewLocalProjectPageViewModelDesign : NewLocalProjectPageViewModel {
        public NewLocalProjectPageViewModelDesign() : base(new()) {
            Location = Path.GetFullPath(Path.Combine((new WorldBuilderSettings()).DataPath, "Projects"));
        }
    }
}
