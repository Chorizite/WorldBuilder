using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Tests.Mocks {
    public class MockDatRepositoryService : IDatRepositoryService {
        public string RepositoryRoot => "";

        public Task<Result<Unit>> DeleteAsync(Guid id, CancellationToken ct) {
            return Task.FromResult(Result<Unit>.Success(Unit.Value));
        }

        public ManagedDatSet? GetManagedDataSet(Guid id) {
            return null;
        }

        public IReadOnlyList<ManagedDatSet> GetManagedDataSets() {
            return [];
        }

        public string GetDatSetPath(Guid id, string projectDirectory) {
            return "";
        }

        public Task<IReadOnlyList<string>> GetProjectsUsingAsync(Guid id, string searchRoot, CancellationToken ct) {
            return Task.FromResult((IReadOnlyList<string>)new List<string>());
        }

        public Task<Result<ManagedDatSet>> ImportAsync(string sourceDirectory, string? friendlyName, IProgress<(string message, float progress)>? progress, CancellationToken ct) {
            return Task.FromResult(Result<ManagedDatSet>.Success(new ManagedDatSet()));
        }

        public Task<Result<(Guid id, ManagedDatSet metadata)>> CalculateDeterministicIdAsync(string directory, CancellationToken ct) {
            return Task.FromResult(Result<(Guid id, ManagedDatSet metadata)>.Success((Guid.NewGuid(), new ManagedDatSet())));
        }

        public Task<Result<Unit>> UpdateFriendlyNameAsync(Guid id, string newFriendlyName, CancellationToken ct) {
            return Task.FromResult(Result<Unit>.Success(Unit.Value));
        }

        public IDatReaderWriter GetDatReaderWriter(string datSetPath) {
            return new MockDatReaderWriter();
        }

        public void SetRepositoryRoot(string rootDirectory) {
        }
    }
}
