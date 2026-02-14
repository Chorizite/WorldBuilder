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

        public static readonly StyledProperty<bool> Is3DProperty =
            AvaloniaProperty.Register<DataIdPreview, bool>(nameof(Is3D));

        public bool Is3D {
            get => GetValue(Is3DProperty);
            private set => SetValue(Is3DProperty, value);
        }

        public static readonly StyledProperty<bool> Is2DProperty =
            AvaloniaProperty.Register<DataIdPreview, bool>(nameof(Is2D));

        public bool Is2D {
            get => GetValue(Is2DProperty);
            private set => SetValue(Is2DProperty, value);
        }

        public static readonly StyledProperty<bool> IsSetupProperty =
            AvaloniaProperty.Register<DataIdPreview, bool>(nameof(IsSetup));

        public bool IsSetup {
            get => GetValue(IsSetupProperty);
            private set => SetValue(IsSetupProperty, value);
        }

        public static readonly StyledProperty<bool> IsPreviewableProperty =
            AvaloniaProperty.Register<DataIdPreview, bool>(nameof(IsPreviewable));

        public bool IsPreviewable {
            get => GetValue(IsPreviewableProperty);
            private set => SetValue(IsPreviewableProperty, value);
        }

        public static readonly StyledProperty<bool> HasDataProperty =
            AvaloniaProperty.Register<DataIdPreview, bool>(nameof(HasData));

        public bool HasData {
            get => GetValue(HasDataProperty);
            private set => SetValue(HasDataProperty, value);
        }

        public static readonly StyledProperty<DBObjType> DataObjectTypeProperty =
            AvaloniaProperty.Register<DataIdPreview, DBObjType>(nameof(DataObjectType));

        public DBObjType DataObjectType {
            get => GetValue(DataObjectTypeProperty);
            private set => SetValue(DataObjectTypeProperty, value);
        }

        public static readonly StyledProperty<string?> PreviewDetailsProperty =
            AvaloniaProperty.Register<DataIdPreview, string?>(nameof(PreviewDetails));

        public string? PreviewDetails {
            get => GetValue(PreviewDetailsProperty);
            private set => SetValue(PreviewDetailsProperty, value);
        }

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
                IsPreviewable = false;
                HasData = false;
                DataObjectType = DBObjType.Unknown;
                PreviewDetails = null;
                TextureBitmap = null;
                return;
            }

            HasData = true;
            var type = Dats.TypeFromId(DataId);
            DataObjectType = type;
            PreviewDetails = null;
            
            IsSetup = type == DBObjType.Setup;
            Is3D = IsSetup || type == DBObjType.GfxObj;
            Is2D = type == DBObjType.SurfaceTexture || type == DBObjType.RenderSurface;

            IsPreviewable = Is3D || Is2D;

            if (Is2D) {
                var textureService = WorldBuilder.App.ProjectManager?.GetProjectService<TextureService>();
                if (textureService != null) {
                    TextureBitmap = await textureService.GetTextureAsync(DataId);
                }

                if (DataObjectType == DBObjType.RenderSurface) {
                    if (Dats.HighRes.TryGet<DatReaderWriter.DBObjs.RenderSurface>(DataId, out var surf)) {
                        PreviewDetails = $"{surf.Width}x{surf.Height} {surf.Format}";
                    }
                    else if (Dats.Portal.TryGet<DatReaderWriter.DBObjs.RenderSurface>(DataId, out var surf2)) {
                        PreviewDetails = $"{surf2.Width}x{surf2.Height} {surf2.Format}";
                    }
                } else if (DataObjectType == DBObjType.SurfaceTexture) {
                    if (Dats.Portal.TryGet<DatReaderWriter.DBObjs.SurfaceTexture>(DataId, out var surfTex)) {
                        PreviewDetails = $"{surfTex.Textures.Count} textures";
                    }
                }
            } else {
                TextureBitmap = null;
            }
        }
    }
}
