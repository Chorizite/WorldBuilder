using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Numerics;
using System.Threading.Tasks;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Views {
    public partial class DatObjectPreview : UserControl {
        public static readonly StyledProperty<uint> DataIdProperty =
            AvaloniaProperty.Register<DatObjectPreview, uint>(nameof(DataId));

        public uint DataId {
            get => GetValue(DataIdProperty);
            set => SetValue(DataIdProperty, value);
        }

        public static readonly StyledProperty<IDatReaderWriter?> DatsProperty =
            AvaloniaProperty.Register<DatObjectPreview, IDatReaderWriter?>(nameof(Dats));

        public IDatReaderWriter? Dats {
            get => GetValue(DatsProperty);
            set => SetValue(DatsProperty, value);
        }

        public static readonly StyledProperty<Type?> TargetTypeProperty =
            AvaloniaProperty.Register<DatObjectPreview, Type?>(nameof(TargetType));

        public Type? TargetType {
            get => GetValue(TargetTypeProperty);
            set => SetValue(TargetTypeProperty, value);
        }

        public static readonly StyledProperty<bool> IsTooltipProperty =
            AvaloniaProperty.Register<DatObjectPreview, bool>(nameof(IsTooltip));

        public bool IsTooltip {
            get => GetValue(IsTooltipProperty);
            set => SetValue(IsTooltipProperty, value);
        }

        public static readonly StyledProperty<IBrush?> ClearColorProperty =
            AvaloniaProperty.Register<DatObjectPreview, IBrush?>(nameof(ClearColor));

        public IBrush? ClearColor {
            get => GetValue(ClearColorProperty);
            set => SetValue(ClearColorProperty, value);
        }

        public static readonly StyledProperty<bool> Is3DProperty =
            AvaloniaProperty.Register<DatObjectPreview, bool>(nameof(Is3D));

        public bool Is3D {
            get => GetValue(Is3DProperty);
            private set => SetValue(Is3DProperty, value);
        }

        public static readonly StyledProperty<bool> Is2DProperty =
            AvaloniaProperty.Register<DatObjectPreview, bool>(nameof(Is2D));

        public bool Is2D {
            get => GetValue(Is2DProperty);
            private set => SetValue(Is2DProperty, value);
        }

        public static readonly StyledProperty<bool> IsSetupProperty =
            AvaloniaProperty.Register<DatObjectPreview, bool>(nameof(IsSetup));

        public bool IsSetup {
            get => GetValue(IsSetupProperty);
            private set => SetValue(IsSetupProperty, value);
        }

        public static readonly StyledProperty<bool> IsPreviewableProperty =
            AvaloniaProperty.Register<DatObjectPreview, bool>(nameof(IsPreviewable));

        public bool IsPreviewable {
            get => GetValue(IsPreviewableProperty);
            private set => SetValue(IsPreviewableProperty, value);
        }

        public static readonly StyledProperty<bool> HasDataProperty =
            AvaloniaProperty.Register<DatObjectPreview, bool>(nameof(HasData));

        public bool HasData {
            get => GetValue(HasDataProperty);
            private set => SetValue(HasDataProperty, value);
        }

        public static readonly StyledProperty<DBObjType> DataObjectTypeProperty =
            AvaloniaProperty.Register<DatObjectPreview, DBObjType>(nameof(DataObjectType));

        public DBObjType DataObjectType {
            get => GetValue(DataObjectTypeProperty);
            private set => SetValue(DataObjectTypeProperty, value);
        }

        public static readonly StyledProperty<string?> PreviewDetailsProperty =
            AvaloniaProperty.Register<DatObjectPreview, string?>(nameof(PreviewDetails));

        public string? PreviewDetails {
            get => GetValue(PreviewDetailsProperty);
            private set => SetValue(PreviewDetailsProperty, value);
        }

        public static readonly DirectProperty<DatObjectPreview, Bitmap?> TextureBitmapProperty =
            AvaloniaProperty.RegisterDirect<DatObjectPreview, Bitmap?>(nameof(TextureBitmap), o => o.TextureBitmap);

        private Bitmap? _textureBitmap;
        public Bitmap? TextureBitmap {
            get => _textureBitmap;
            private set {
                var old = _textureBitmap;
                SetAndRaise(TextureBitmapProperty, ref _textureBitmap, value);
                if (old != null && !ReferenceEquals(old, value)) {
                    old.Dispose();
                }
            }
        }

        public static readonly StyledProperty<Stretch> ImageStretchProperty =
            AvaloniaProperty.Register<DatObjectPreview, Stretch>(nameof(ImageStretch), Stretch.Uniform);

        public Stretch ImageStretch {
            get => GetValue(ImageStretchProperty);
            set => SetValue(ImageStretchProperty, value);
        }

        public static readonly StyledProperty<double> ZoomLevelProperty =
            AvaloniaProperty.Register<DatObjectPreview, double>(nameof(ZoomLevel), 1.0);

        public double ZoomLevel {
            get => GetValue(ZoomLevelProperty);
            set => SetValue(ZoomLevelProperty, value);
        }

        public static readonly StyledProperty<double> ZoomedWidthProperty =
            AvaloniaProperty.Register<DatObjectPreview, double>(nameof(ZoomedWidth), double.NaN);

        public double ZoomedWidth {
            get => GetValue(ZoomedWidthProperty);
            set => SetValue(ZoomedWidthProperty, value);
        }

        public static readonly StyledProperty<double> ZoomedHeightProperty =
            AvaloniaProperty.Register<DatObjectPreview, double>(nameof(ZoomedHeight), double.NaN);

        public double ZoomedHeight {
            get => GetValue(ZoomedHeightProperty);
            set => SetValue(ZoomedHeightProperty, value);
        }

        public static readonly StyledProperty<bool> IsManualZoomProperty =
            AvaloniaProperty.Register<DatObjectPreview, bool>(nameof(IsManualZoom), false);

        public bool IsManualZoom {
            get => GetValue(IsManualZoomProperty);
            set => SetValue(IsManualZoomProperty, value);
        }

        public static readonly StyledProperty<bool> ShowWireframeProperty =
            AvaloniaProperty.Register<DatObjectPreview, bool>(nameof(ShowWireframe), false);

        public bool ShowWireframe {
            get => GetValue(ShowWireframeProperty);
            set => SetValue(ShowWireframeProperty, value);
        }

        public static readonly StyledProperty<Vector4> WireframeColorProperty =
            AvaloniaProperty.Register<DatObjectPreview, Vector4>(nameof(WireframeColor), new Vector4(0.0f, 1.0f, 0.0f, 0.5f));

        public Vector4 WireframeColor {
            get => GetValue(WireframeColorProperty);
            set => SetValue(WireframeColorProperty, value);
        }

        public static readonly StyledProperty<bool> ShowCullingProperty =
            AvaloniaProperty.Register<DatObjectPreview, bool>(nameof(ShowCulling), true);

        public bool ShowCulling {
            get => GetValue(ShowCullingProperty);
            set => SetValue(ShowCullingProperty, value);
        }

        public DatObjectPreview() {
            InitializeComponent();
            AddHandler(PointerWheelChangedEvent, (s, e) => {
                if (Is2D && !IsTooltip && TextureBitmap != null) {
                    if (!IsManualZoom) {
                        // Calculate initial zoom level from "Fit" size
                        var scrollViewer = this.FindControl<ScrollViewer>("PreviewScrollViewer");
                        if (scrollViewer != null) {
                            var viewport = scrollViewer.Viewport;
                            var texSize = TextureBitmap.Size;
                            var scaleX = (viewport.Width - 10) / texSize.Width; // subtract margin
                            var scaleY = (viewport.Height - 10) / texSize.Height;
                            ZoomLevel = Math.Min(scaleX, scaleY);
                        }
                        IsManualZoom = true;
                        ImageStretch = Stretch.Fill;
                    }

                    var delta = e.Delta.Y;
                    if (delta > 0) {
                        ZoomLevel *= 1.1;
                    }
                    else {
                        ZoomLevel /= 1.1;
                    }
                    UpdateZoomedSize();
                    e.Handled = true;
                }
            }, Avalonia.Interactivity.RoutingStrategies.Bubble);
        }

        public void SetStretch(Stretch stretch) {
            IsManualZoom = stretch != Stretch.Uniform;
            ImageStretch = stretch;
            ZoomLevel = 1.0;
            UpdateZoomedSize();
        }

        private void UpdateZoomedSize() {
            if (TextureBitmap == null || !IsManualZoom) {
                ZoomedWidth = double.NaN;
                ZoomedHeight = double.NaN;
            }
            else {
                ZoomedWidth = TextureBitmap.Size.Width * ZoomLevel;
                ZoomedHeight = TextureBitmap.Size.Height * ZoomLevel;
            }
        }

        public void On1To1Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => SetStretch(Stretch.Fill);
        public void OnFitClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => SetStretch(Stretch.Uniform);

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) {
            base.OnDetachedFromVisualTree(e);
            TextureBitmap = null;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == DataIdProperty || change.Property == DatsProperty || change.Property == TargetTypeProperty) {
                UpdatePreview();
            }
            else if (change.Property == TextureBitmapProperty || change.Property == ZoomLevelProperty || change.Property == ImageStretchProperty) {
                UpdateZoomedSize();
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

            var resolutions = Dats.ResolveId(DataId).ToList();
            if (resolutions.Count == 0) {
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

            IDatReaderWriter.IdResolution selectedResolution = resolutions.First();
            if (TargetType != null) {
                var targetTypeName = TargetType.Name;
                var matching = resolutions.FirstOrDefault(r => r.Type.ToString() == targetTypeName);
                if (matching != null) {
                    selectedResolution = matching;
                }
            }

            HasData = true;
            var type = selectedResolution.Type;
            var db = selectedResolution.Database;
            DataObjectType = type;
            PreviewDetails = null;

            IsSetup = type == DBObjType.Setup || type == DBObjType.EnvCell;
            Is3D = IsSetup || type == DBObjType.GfxObj;
            Is2D = type == DBObjType.SurfaceTexture || type == DBObjType.RenderSurface || type == DBObjType.Surface;

            IsPreviewable = Is3D || Is2D;

            if (Is2D) {
                var textureService = WorldBuilder.App.ProjectManager?.GetProjectService<TextureService>();
                if (textureService != null) {
                    if (DataObjectType == DBObjType.Surface) {
                        if (db.TryGet<Surface>(DataId, out var surface)) {
                            bool isClipMap = surface.Type.HasFlag(SurfaceType.Base1ClipMap);
                            if (surface.OrigTextureId != 0) {
                                TextureBitmap = await textureService.GetTextureAsync(surface.OrigTextureId, surface.OrigPaletteId, isClipMap);
                            }
                            else if (surface.Type.HasFlag(SurfaceType.Base1Solid)) {
                                TextureBitmap = textureService.CreateSolidColorBitmap(surface.ColorValue);
                            }
                        }
                    }
                    else {
                        TextureBitmap = await textureService.GetTextureAsync(DataId);
                    }
                }

                if (DataObjectType == DBObjType.RenderSurface) {
                    if (db.TryGet<RenderSurface>(DataId, out var surf)) {
                        PreviewDetails = $"{surf.Width}x{surf.Height} {surf.Format}";
                    }
                }
                else if (DataObjectType == DBObjType.SurfaceTexture) {
                    if (db.TryGet<SurfaceTexture>(DataId, out var surfTex)) {
                        PreviewDetails = $"{surfTex.Textures.Count} textures";
                    }
                }
                else if (DataObjectType == DBObjType.Surface) {
                    if (db.TryGet<Surface>(DataId, out var surface)) {
                        PreviewDetails = $"{surface.Type}";
                    }
                }
            }
            else {
                TextureBitmap = null;
            }
        }
    }
}
