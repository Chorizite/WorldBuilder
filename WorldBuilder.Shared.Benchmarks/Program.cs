using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Repositories;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Shared.Benchmarks {
    [MemoryDiagnoser]
    public class LoadAllLandblocks {
        private readonly DefaultDatReaderWriter _dats;
        private readonly SQLiteProjectRepository _repo;
        private readonly DocumentManager _manager;

        public LoadAllLandblocks() {
            _dats = new DefaultDatReaderWriter(@"C:\ac\dats\EoR\");
            _repo = new SQLiteProjectRepository("Data Source=:memory:");
            _manager = new DocumentManager(_repo, _dats, new NullLogger<DocumentManager>());
        }

        [Benchmark]
        public void Load() {

            var _terrain = new LandscapeDocument(1u);
            _terrain.InitializeForEditingAsync(_dats, _manager, default).GetAwaiter().GetResult();
        }
    }

    public class Program {
        public static void Main(string[] args) {
            var summary = BenchmarkRunner.Run<LoadAllLandblocks>();
        }
    }
}