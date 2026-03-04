using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanumanInstitute.MvvmDialogs;

using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.Landscape.ViewModels {
    public partial class TextInputDialogViewModel : ViewModelBase, IModalDialogViewModel {
        [ObservableProperty]
        private string _promptText = "Enter text:";

        [ObservableProperty]
        private string _buttonText = "OK";

        [ObservableProperty]
        private string _inputText = string.Empty;

        public bool? DialogResult { get; set; }

        public event EventHandler? RequestClose;

        public TextInputDialogViewModel(string promptText = "Enter text:", string initialText = "", string buttonText = "OK") {
            _promptText = promptText;
            _inputText = initialText;
            _buttonText = buttonText;
        }

        [RelayCommand]
        private void Confirm() {
            DialogResult = true;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }

        [RelayCommand]
        private void Cancel() {
            DialogResult = false;
            RequestClose?.Invoke(this, EventArgs.Empty);
        }
    }
}
