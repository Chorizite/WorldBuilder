using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using WorldBuilder.ViewModels;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class ParticleEmitterOverviewViewModel : ViewModelBase {
        [ObservableProperty]
        private ParticleEmitter _particleEmitter;

        private readonly IDatReaderWriter _dats;

        public EmitterType EmitterType => ParticleEmitter.EmitterType;
        public ParticleType ParticleType => ParticleEmitter.ParticleType;
        public int MaxParticles => ParticleEmitter.MaxParticles;
        public double Birthrate => ParticleEmitter.Birthrate;
        public double Lifespan => ParticleEmitter.Lifespan;

        public IDatReaderWriter Dats => _dats;

        public ParticleEmitterOverviewViewModel(ParticleEmitter particleEmitter, IDatReaderWriter dats) {
            _particleEmitter = particleEmitter;
            _dats = dats;
        }
    }
}
