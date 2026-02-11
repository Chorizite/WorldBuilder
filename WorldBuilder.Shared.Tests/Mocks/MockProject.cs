using Moq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Modules.Landscape;
using WorldBuilder.Shared.Repositories;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Tests.Mocks {
    internal class MockProject : IProject {
        public string Name { get; init; }
        public ServiceProvider Services { get; init; }
        public IDocumentManager Documents { get; init; }

        public LandscapeModule Landscape => throw new NotImplementedException();

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
        private MockProject(string name) {
            Name = name;
        }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

        public static IProject Create(string? projectName = null) {
            return (IProject)new MockProject(projectName ?? "MockProject");
        }

        public Task<DocumentRental<LandscapeDocument>> GetOrCreateTerrainDocumentAsync(uint regionId, CancellationToken ct) {
            throw new NotImplementedException();
        }

        public void Dispose() {
            Services?.Dispose();
        }
    }
}