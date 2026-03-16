using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.ViewModels
{
    /// <summary>
    /// View model for a single managed keyword database.
    /// </summary>
    public partial class ManagedKeywordDbViewModel : ObservableObject {
        private readonly ManagedKeywordDb _model;
        private readonly IDatRepositoryService _datRepository;
        private readonly IAceRepositoryService _aceRepository;
        private readonly IKeywordRepositoryService _keywordRepository;
        private readonly ILogger _log;

        public Guid DatSetId => _model.DatSetId;
        public Guid AceDbId => _model.AceDbId;
        public int GeneratorVersion => _model.GeneratorVersion;
        public DateTime LastGenerated => _model.LastGenerated;

        public string DatSetName => _datRepository.GetManagedDataSet(DatSetId)?.FriendlyName ?? DatSetId.ToString()[..8];
        public string AceDbName => _aceRepository.GetManagedAceDb(AceDbId)?.FriendlyName ?? AceDbId.ToString()[..8];

        public ManagedKeywordDbViewModel(ManagedKeywordDb model, IDatRepositoryService datRepository, IAceRepositoryService aceRepository, IKeywordRepositoryService keywordRepository, ILogger log) {
            _model = model;
            _datRepository = datRepository;
            _aceRepository = aceRepository;
            _keywordRepository = keywordRepository;
            _log = log;
        }
    }
}