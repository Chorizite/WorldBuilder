using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.ViewModels
{
    /// <summary>
    /// View model for a single managed DAT set in the management view.
    /// </summary>
    public partial class ManagedDatSetViewModel : ObservableObject {
        private readonly ManagedDatSet _model;
        private readonly IDatRepositoryService _datRepository;
        private readonly ILogger _log;

        [ObservableProperty]
        private string _friendlyName;

        [ObservableProperty]
        private bool _isEditing;

        public Guid Id => _model.Id;
        public int PortalIteration => _model.PortalIteration;
        public int CellIteration => _model.CellIteration;
        public int HighResIteration => _model.HighResIteration;
        public int LanguageIteration => _model.LanguageIteration;
        public string CombinedMd5 => _model.CombinedMd5;
        public DateTime ImportDate => _model.ImportDate;

        public string DisplayIteration => $"P:{PortalIteration} C:{CellIteration} H:{HighResIteration} L:{LanguageIteration}";

        public ManagedDatSetViewModel(ManagedDatSet model, IDatRepositoryService datRepository, ILogger log) {
            _model = model;
            _datRepository = datRepository;
            _log = log;
            _friendlyName = model.FriendlyName;
        }

        [RelayCommand]
        private void StartEdit() => IsEditing = true;

        [RelayCommand]
        private async Task SaveEdit() {
            if (string.IsNullOrWhiteSpace(FriendlyName)) {
                FriendlyName = _model.FriendlyName;
                IsEditing = false;
                return;
            }

            var result = await _datRepository.UpdateFriendlyNameAsync(Id, FriendlyName, CancellationToken.None);
            if (result.IsSuccess) {
                IsEditing = false;
            }
            else {
                _log.LogError("Failed to update friendly name: {Error}", result.Error.Message);
            }
        }

        [RelayCommand]
        private void CancelEdit() {
            FriendlyName = _model.FriendlyName;
            IsEditing = false;
        }
    }
}