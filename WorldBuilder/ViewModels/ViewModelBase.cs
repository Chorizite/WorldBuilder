using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace WorldBuilder.ViewModels;

public abstract class ViewModelBase : ObservableValidator {
    protected TopLevel TopLevel {
        get {

            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                return TopLevel.GetTopLevel(desktop.MainWindow)!;
            else if (Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime singleView)
                return TopLevel.GetTopLevel(singleView.MainView)!;

            throw new InvalidOperationException("Application lifetime is not supported! Could not get TopLevel.");
        }
    }
}
