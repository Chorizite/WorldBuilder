using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder.ViewModels.Editors {
    public partial class LandscapeEditorViewModel : ViewModelBase {
        [ObservableProperty]
        public string _test = "test";

        public LandscapeEditorViewModel() {
            
        }
    }
}
