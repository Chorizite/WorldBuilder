using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Factories {
    public enum PageName {
        GettingStarted,
        NewLocalProject
    }

    public class PageFactory {
        private readonly Func<PageName, PageViewModel> _pageFactory;

        public WindowViewModel CurrentWindow { get; internal set; }

        public PageFactory(Func<PageName, PageViewModel> pageFactory) {
            _pageFactory = pageFactory;
        }

        public PageViewModel GetViewModel(PageName page) => _pageFactory.Invoke(page);
    }
}
