using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.ViewModels
{
    /// <summary>
    /// View model for a single managed ACE DB in the management view.
    /// </summary>
    public partial class ManagedAceDbViewModel : ObservableObject {
        private readonly ManagedAceDb _model;
        private readonly IAceRepositoryService _aceRepository;
        private readonly ILogger _log;

        [ObservableProperty]
        private string _friendlyName;

        [ObservableProperty]
        private bool _isEditing;

        public Guid Id => _model.Id;
        public string BaseVersion => _model.BaseVersion;
        public string PatchVersion => _model.PatchVersion;
        public string LastModified => _model.LastModified;
        public string Md5 => _model.Md5;
        public DateTime ImportDate => _model.ImportDate;
        public string DisplayVersion => _model.DisplayVersion;

        public ManagedAceDbViewModel(ManagedAceDb model, IAceRepositoryService aceRepository, ILogger log) {
            _model = model;
            _aceRepository = aceRepository;
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

            var result = await _aceRepository.UpdateFriendlyNameAsync(Id, FriendlyName, CancellationToken.None);
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