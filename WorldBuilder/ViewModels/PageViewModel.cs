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
    public abstract class PageViewModel : BaseViewModel {
        public abstract string WindowName { get; }

        public WindowViewModel? ParentWindow { get; set; }
    }
}
