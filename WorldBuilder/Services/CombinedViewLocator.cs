using Avalonia.Controls;
using Avalonia.Controls.Templates;
using HanumanInstitute.MvvmDialogs.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using System;
using WorldBuilder.Lib;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Services;

/// <summary>
/// Combined view locator that handles both regular views and dialog windows.
/// Implements Avalonia's IDataTemplate for general view resolution and extends ViewLocatorBase for dialog support.
/// </summary>
public class CombinedViewLocator : ViewLocatorBase, IDataTemplate {
    public override Control Build(object? data) {
        if (data is null) {
            return new TextBlock { Text = "data was null" };
        }

        // Use our custom GetViewName logic to determine the view type
        var name = GetViewName(data);
#pragma warning disable IL2057 // Unrecognized value passed to the parameter of method. It's not possible to guarantee the availability of the target type.
        var type = Type.GetType(name);
#pragma warning restore IL2057 // Unrecognized value passed to the parameter of method. It's not possible to guarantee the availability of the target type.

        if (type == null) {
            return new TextBlock { Text = "Not Found: " + name };
        }

        var control = App.Services?.GetService<ProjectManager>()?.GetProjectService<Control>(type);
        if (control != null) {
            return (Control)control!;
        }

        control = App.Services?.GetService(type) as Control;

        if (control != null) {
            return (Control)control!;
        }

        control = Activator.CreateInstance(type) as Control;

        if (control != null) {
            return (Control)control!;
        }

        return new TextBlock { Text = $"Not Found: {type.Name} {name}" };
    }

    public override bool Match(object? data) {
        return data is ViewModelBase;
    }

    /// <inheritdoc />
    protected override string GetViewName(object viewModel) {
        var viewModelName = viewModel.GetType().FullName!;

        if (viewModelName.EndsWith("WindowViewModel")) {
            return viewModelName.Replace(".ViewModels.", ".Views.").Replace("ViewModel", "");
        }

        return viewModelName.Replace(".ViewModels.", ".Views.").Replace("ViewModel", "View");
    }
}
