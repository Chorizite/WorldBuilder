using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Factories;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.ViewModels.Pages;

namespace WorldBuilder.ViewModels {
    public partial class GettingStartedWindowViewModel : WindowViewModel {

        public GettingStartedWindowViewModel(PageFactory pageFactory) : base(pageFactory) {
            NavigateToPage(PageName.GettingStarted);
        }
    }
}
