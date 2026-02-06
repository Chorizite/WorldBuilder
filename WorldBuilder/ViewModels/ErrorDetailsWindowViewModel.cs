using CommunityToolkit.Mvvm.ComponentModel;
using HanumanInstitute.MvvmDialogs;
using System.Threading.Tasks;

namespace WorldBuilder.ViewModels
{
    public partial class ErrorDetailsWindowViewModel : ViewModelBase, IModalDialogViewModel
    {
        [ObservableProperty]
        private string _errorText = string.Empty;

        public bool? DialogResult { get; set; }

        public ErrorDetailsWindowViewModel(string errorText)
        {
            ErrorText = errorText;
        }
    }
}