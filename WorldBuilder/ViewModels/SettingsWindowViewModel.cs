using CommunityToolkit.Mvvm.ComponentModel;
using HanumanInstitute.MvvmDialogs;
using WorldBuilder.Services;
using System;

namespace WorldBuilder.ViewModels
{
    public partial class SettingsWindowViewModel : ViewModelBase
    {
        public WorldBuilderSettings Settings { get; }

        public event EventHandler? Closed;

        public SettingsWindowViewModel(WorldBuilderSettings settings)
        {
            Settings = settings;
        }

        public void OnClosed() => Closed?.Invoke(this, EventArgs.Empty);
    }
}