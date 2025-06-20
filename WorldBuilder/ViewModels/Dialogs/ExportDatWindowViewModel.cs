using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.ViewModels.Dialogs {
    public partial class ExportDatWindowViewModel : BaseViewModel {
        public Window ParentWindow { get; set; }

        [ObservableProperty]
        private Project _project;

        [ObservableProperty]
        private string _exportDirectory = String.Empty;

        [ObservableProperty]
        private int _cellIteration = 0;

        [ObservableProperty]
        private int _portalIteration = 0;

        [ObservableProperty]
        private int _languageIteration = 0;

        [ObservableProperty]
        private int _highResIteration = 0;

        public ExportDatWindowViewModel(Project project) {
            Project = project;
            CellIteration = Project.Dats.Cell.Iteration.CurrentIteration;
            PortalIteration = Project.Dats.Portal.Iteration.CurrentIteration;
            LanguageIteration = Project.Dats.Local.Iteration.CurrentIteration;
            HighResIteration = Project.Dats.HighRes.Iteration.CurrentIteration;
        }

        [RelayCommand]
        private async Task SelectBaseDatDirectory() {
            var topLevel = TopLevel.GetTopLevel(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null);

            if (topLevel == null || topLevel.StorageProvider.CanPickFolder == false) return;
            var dir = string.IsNullOrWhiteSpace(ExportDirectory) ? Path.GetDirectoryName(GetType().Assembly.Location) : ExportDirectory;
            var res = await topLevel.StorageProvider.OpenFolderPickerAsync(new() {
                AllowMultiple = false,
                Title = "Select Export Directory",
                SuggestedStartLocation = await topLevel.StorageProvider.TryGetFolderFromPathAsync(new Uri(dir))
            });

            if (res.Count == 1) {
                ExportDirectory = res.First().TryGetLocalPath() ?? "";
            }
        }

        [RelayCommand]
        private void GoBack() => ParentWindow?.Close();

        [RelayCommand]
        private void ExportDats() {
            Project.ExportDats(ExportDirectory, CellIteration, PortalIteration, LanguageIteration, HighResIteration);
            GoBack();
        }
    }
}
