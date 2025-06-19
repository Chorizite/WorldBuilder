using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Factories;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.ViewModels {
    internal partial class ProjectWindowViewModel : WindowViewModel {
        [ObservableProperty]
        private Project _project;

        public ProjectWindowViewModel(PageFactory pageFactory, Project project) : base(pageFactory) {
            Project = project;

        }
    }
}
