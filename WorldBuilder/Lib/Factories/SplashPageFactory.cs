using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Lib.Factories {
    public class SplashPageFactory {
        public SplashPageFactory() {
        }

        public T Create<T>() where T : SplashPageViewModelBase {
            return App.Services.GetRequiredService<T>();
        }
    }
}
