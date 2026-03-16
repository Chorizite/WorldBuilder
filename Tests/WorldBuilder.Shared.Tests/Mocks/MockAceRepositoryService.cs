using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Tests.Mocks {
    public class MockAceRepositoryService : IAceRepositoryService {
        public string RepositoryRoot => "";

        public Task<Result<Unit>> DeleteAsync(Guid id, CancellationToken ct) {
            return Task.FromResult(Result<Unit>.Success(Unit.Value));
        }

        public ManagedAceDb? GetManagedAceDb(Guid id) {
            return null;
        }

        public IReadOnlyList<ManagedAceDb> GetManagedAceDbs() {
            return [];
        }

        public string GetAceDbPath(Guid id, string projectDirectory) {
            return "";
        }

        public Task<Result<ManagedAceDb>> ImportAsync(string sourcePath, string? friendlyName, IProgress<(string message, float progress)>? progress, CancellationToken ct) {
            return Task.FromResult(Result<ManagedAceDb>.Success(new ManagedAceDb()));
        }

        public Task<Result<ManagedAceDb>> DownloadLatestAsync(IProgress<(string message, float progress)>? progress, CancellationToken ct) {
            return Task.FromResult(Result<ManagedAceDb>.Success(new ManagedAceDb()));
        }

        public Task<Result<Unit>> UpdateFriendlyNameAsync(Guid id, string newFriendlyName, CancellationToken ct) {
            return Task.FromResult(Result<Unit>.Success(Unit.Value));
        }

        public void SetRepositoryRoot(string rootDirectory) {
        }
    }
}
