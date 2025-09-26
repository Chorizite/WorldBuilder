using Microsoft.Extensions.DependencyInjection;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Windows.Lib.Extensions {
    public static class ServiceCollectionExtensions {
        public static IServiceCollection AddWorldBuilderApp(this IServiceCollection collection) {
            //collection.AddSingleton<GL>(o => ._gl);
            return collection;
        }
    }
}
