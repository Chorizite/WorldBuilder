using CommunityToolkit.Mvvm.ComponentModel;
using WorldBuilder.Factories;

namespace WorldBuilder.ViewModels {
    public abstract partial class WindowViewModel : BaseViewModel {
        protected PageFactory _pageFactory;

        [ObservableProperty]
        private PageViewModel _currentPage;

        public WindowViewModel(PageFactory pageFactory) {
            _pageFactory = pageFactory;
            _pageFactory.CurrentWindow = this;
        }

        public virtual void NavigateToPage(PageName pageName) {
            CurrentPage = _pageFactory.GetViewModel(pageName);
            CurrentPage.ParentWindow = this;
        }
    }
}