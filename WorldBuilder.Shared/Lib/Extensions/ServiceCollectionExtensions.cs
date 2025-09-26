using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Documents;

namespace WorldBuilder.Shared.Lib.Extensions {
    public static class ServiceCollectionExtensions {
        public static IServiceCollection AddWorldBuilder(this IServiceCollection collection) {
            return collection;
        }
    }
}