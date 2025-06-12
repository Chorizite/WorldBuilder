using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorldBuilder.ViewModels {
    public class FunViewModel : ViewModelBase {
        private float _goldMetallness = 0.9f;
        private float _goldRoughness = 0.15f;
        private float _goldOcclusion = 1.0f;

        private float _silverMetallness = 0.8f;
        private float _silverRoughness = 0.6f;
        private float _silverOcclusion = 1.0f;
        
        private bool _useTexAlbedo = true;
        private bool _useTexNormal = false;
        private bool _useTexMRA = true;
        private bool _useTexEmissive = false;

        private float _lightIntensity = 1.0f;
        private float _ambientIntensity = 0.1f;

        public float AmbientIntensity {
            get => _ambientIntensity;
            set {
                this.RaiseAndSetIfChanged(ref _ambientIntensity, value);
            }
        }

        public float LightIntensity {
            get => _lightIntensity;
            set {
                this.RaiseAndSetIfChanged(ref _lightIntensity, value);
            }
        }

        public bool UseTexAlbedo {
            get => _useTexAlbedo;
            set {
                this.RaiseAndSetIfChanged(ref _useTexAlbedo, value);
            }
        }

        public bool UseTexNormal {
            get => _useTexNormal;
            set {
                this.RaiseAndSetIfChanged(ref _useTexNormal, value);
            }
        }

        public bool UseTexMRA {
            get => _useTexMRA;
            set {
                this.RaiseAndSetIfChanged(ref _useTexMRA, value);
            }
        }

        public bool UseTexEmissive {
            get => _useTexEmissive;
            set {
                this.RaiseAndSetIfChanged(ref _useTexEmissive, value);
            }
        }

        public float GoldMetallness {
            get => _goldMetallness;
            set {
                this.RaiseAndSetIfChanged(ref _goldMetallness, value);
            }
        }

        public float GoldRoughness {
            get => _goldRoughness;
            set {
                this.RaiseAndSetIfChanged(ref _goldRoughness, value);
            }
        }

        public float GoldOcclusion {
            get => _goldOcclusion;
            set {
                this.RaiseAndSetIfChanged(ref _goldOcclusion, value);
            }
        }

        public float SilverMetallness {
            get => _silverMetallness;
            set {
                this.RaiseAndSetIfChanged(ref _silverMetallness, value);
            }
        }

        public float SilverRoughness {
            get => _silverRoughness;
            set {
                this.RaiseAndSetIfChanged(ref _silverRoughness, value);
            }
        }

        public float SilverOcclusion {
            get => _silverOcclusion;
            set {
                this.RaiseAndSetIfChanged(ref _silverOcclusion, value);
            }
        }

    }
}
