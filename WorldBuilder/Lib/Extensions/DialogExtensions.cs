using System.ComponentModel;
using System.Threading.Tasks;
using HanumanInstitute.MvvmDialogs;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Lib.Extensions;

public static class DialogExtensions
{
    public static async Task<bool?> ShowSettingsWindowAsync(this IDialogService dialog, INotifyPropertyChanged ownerViewModel)
    {
        var viewModel = dialog.CreateViewModel<SettingsWindowViewModel>();
        return await dialog.ShowDialogAsync(ownerViewModel, viewModel);
    }
    
    public static async Task<bool?> ShowExportDatsWindowAsync(this IDialogService dialog, INotifyPropertyChanged ownerViewModel)
    {
        var viewModel = dialog.CreateViewModel<ExportDatsWindowViewModel>();
        return await dialog.ShowDialogAsync(ownerViewModel, viewModel);
    }
}