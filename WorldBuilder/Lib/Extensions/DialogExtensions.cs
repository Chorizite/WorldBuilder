using HanumanInstitute.MvvmDialogs;
using System.ComponentModel;
using System.Threading.Tasks;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Lib.Extensions;

public static class DialogExtensions {
    public static SettingsWindowViewModel ShowSettingsWindow(this IDialogService dialog, INotifyPropertyChanged ownerViewModel) {
        var viewModel = dialog.CreateViewModel<SettingsWindowViewModel>();
        dialog.Show(null, viewModel);
        return viewModel;
    }
}