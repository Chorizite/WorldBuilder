using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using DatReaderWriter.Enums;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Views {
    public partial class DataIdPreview : UserControl {
        public static readonly StyledProperty<uint> DataIdProperty =
            AvaloniaProperty.Register<DataIdPreview, uint>(nameof(DataId));

        public uint DataId {
            get => GetValue(DataIdProperty);
            set => SetValue(DataIdProperty, value);
        }

        public static readonly StyledProperty<IDatReaderWriter?> DatsProperty =
            AvaloniaProperty.Register<DataIdPreview, IDatReaderWriter?>(nameof(Dats));

        public IDatReaderWriter? Dats {
            get => GetValue(DatsProperty);
            set => SetValue(DatsProperty, value);
        }

        public static readonly StyledProperty<bool> IsTooltipProperty =
            AvaloniaProperty.Register<DataIdPreview, bool>(nameof(IsTooltip));

        public bool IsTooltip {
            get => GetValue(IsTooltipProperty);
            set => SetValue(IsTooltipProperty, value);
        }

        public static readonly DirectProperty<DataIdPreview, bool> Is3DProperty =
            AvaloniaProperty.RegisterDirect<DataIdPreview, bool>(nameof(Is3D), o => o.Is3D);

        private bool _is3D;
        public bool Is3D {
            get => _is3D;
            private set => SetAndRaise(Is3DProperty, ref _is3D, value);
        }

        public static readonly DirectProperty<DataIdPreview, bool> Is2DProperty =
            AvaloniaProperty.RegisterDirect<DataIdPreview, bool>(nameof(Is2D), o => o.Is2D);

        private bool _is2D;
        public bool Is2D {
            get => _is2D;
            private set => SetAndRaise(Is2DProperty, ref _is2D, value);
        }

        public static readonly DirectProperty<DataIdPreview, bool> IsSetupProperty =
            AvaloniaProperty.RegisterDirect<DataIdPreview, bool>(nameof(IsSetup), o => o.IsSetup);

        private bool _isSetup;
        public bool IsSetup {
            get => _isSetup;
            private set => SetAndRaise(IsSetupProperty, ref _isSetup, value);
        }

        public static readonly DirectProperty<DataIdPreview, bool> IsPreviewableProperty =
            AvaloniaProperty.RegisterDirect<DataIdPreview, bool>(nameof(IsPreviewable), o => o.IsPreviewable);

        public bool IsPreviewable => Is3D || Is2D;

        public static readonly DirectProperty<DataIdPreview, Bitmap?> TextureBitmapProperty =
            AvaloniaProperty.RegisterDirect<DataIdPreview, Bitmap?>(nameof(TextureBitmap), o => o.TextureBitmap);

        private Bitmap? _textureBitmap;
        public Bitmap? TextureBitmap {
            get => _textureBitmap;
            private set => SetAndRaise(TextureBitmapProperty, ref _textureBitmap, value);
        }

        public DataIdPreview() {
            InitializeComponent();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == DataIdProperty || change.Property == DatsProperty) {
                UpdatePreview();
            }
        }

        private async void UpdatePreview() {
            if (Dats == null || DataId == 0) {
                Is3D = false;
                Is2D = false;
                IsSetup = false;
                TextureBitmap = null;
                return;
            }

            var type = Dats.TypeFromId(DataId);
            
            IsSetup = type == DBObjType.Setup;
            Is3D = IsSetup || type == DBObjType.GfxObj;
            Is2D = type == DBObjType.SurfaceTexture;

            if (Is2D) {
                var textureService = WorldBuilder.App.Services?.GetService<TextureService>();
                if (textureService != null) {
                    TextureBitmap = await textureService.GetTextureAsync(DataId);
                }
            } else {
                TextureBitmap = null;
            }
            
            RaisePropertyChanged(IsPreviewableProperty, !IsPreviewable, IsPreviewable);
        }
    }
}
