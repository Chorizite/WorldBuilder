using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder.ViewModels {
    public abstract class BaseViewModel : ObservableObject {

        protected TopLevel GetTopLevel() {
            return TopLevel.GetTopLevel(Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null);
        }
    }
}
