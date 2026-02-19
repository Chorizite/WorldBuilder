using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HanumanInstitute.MvvmDialogs;
using System;

namespace WorldBuilder.ViewModels;

public partial class TextInputWindowViewModel : ViewModelBase, IModalDialogViewModel {
    [ObservableProperty] private string _title = "Input";
    [ObservableProperty] private string _message = "Enter value:";
    [ObservableProperty] private string _inputText = "";
    
    public bool Result { get; private set; }

    public bool? DialogResult { get; set; }

    public event EventHandler? RequestClose;

    [RelayCommand]
    private void Ok() {
        Result = true;
        DialogResult = true;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel() {
        Result = false;
        DialogResult = false;
        RequestClose?.Invoke(this, EventArgs.Empty);
    }
}
