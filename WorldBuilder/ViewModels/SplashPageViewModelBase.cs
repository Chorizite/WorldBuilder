using CommunityToolkit.Mvvm.ComponentModel;

namespace WorldBuilder.ViewModels {
    /// <summary>
    /// Base class for splash page view models.
    /// </summary>
    public partial class SplashPageViewModelBase : ViewModelBase {
        /// <summary>
        /// Gets or sets a value indicating whether the page is currently loading.
        /// </summary>
        [ObservableProperty]
        private bool _isLoading;

        /// <summary>
        /// Gets or sets the current loading status message.
        /// </summary>
        [ObservableProperty]
        private string _loadingStatus = string.Empty;

        /// <summary>
        /// Gets or sets the current loading progress (0-100).
        /// </summary>
        [ObservableProperty]
        private float _loadingProgress;
    }
}