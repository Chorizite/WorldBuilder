using CommunityToolkit.Mvvm.ComponentModel;
using HanumanInstitute.MvvmDialogs;
using WorldBuilder.Services;

namespace WorldBuilder.ViewModels
{
    public partial class SettingsWindowViewModel : ViewModelBase, IModalDialogViewModel
    {
        public WorldBuilderSettings Settings { get; }

        public bool? DialogResult { get; set; }

        public SettingsWindowViewModel(WorldBuilderSettings settings)
        {
            Settings = settings;
        }
    }
}