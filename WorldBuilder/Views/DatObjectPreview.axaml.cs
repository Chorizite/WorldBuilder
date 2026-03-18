using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Chorizite.OpenGLSDLBackend;
using CommunityToolkit.Mvvm.Input;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Numerics;
using WorldBuilder.Extensions;
using WorldBuilder.Services;
using WorldBuilder.Shared.Lib;
using WorldBuilder.Shared.Models;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Views {
    public partial class DatObjectPreview : UserControl {

        public static readonly StyledProperty<uint> DataIdProperty =
            AvaloniaProperty.Register<DatObjectPreview, uint>(nameof(DataId));

        public uint DataId {
            get => GetValue(DataIdProperty);
            set => SetValue(DataIdProperty, value);
        }

        public DebugRenderSettings RenderSettings => new DebugRenderSettings();

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

        public static readonly StyledProperty<bool> IsPaletteProperty =
            AvaloniaProperty.Register<DatObjectPreview, bool>(nameof(IsPalette));

        public bool IsPalette {
            get => GetValue(IsPaletteProperty);
            private set => SetValue(IsPaletteProperty, value);
        }

        public static readonly StyledProperty<bool> IsPalSetProperty =
            AvaloniaProperty.Register<DatObjectPreview, bool>(nameof(IsPalSet));

        public bool IsPalSet {
            get => GetValue(IsPalSetProperty);
            private set => SetValue(IsPalSetProperty, value);
        }

        public static readonly StyledProperty<bool> IsWaveProperty =
            AvaloniaProperty.Register<DatObjectPreview, bool>(nameof(IsWave));

        public bool IsWave {
            get => GetValue(IsWaveProperty);
            private set => SetValue(IsWaveProperty, value);
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

        private uint _lastUpdateId;

        public static readonly DirectProperty<DatObjectPreview, Bitmap?> TextureBitmapProperty =
            AvaloniaProperty.RegisterDirect<DatObjectPreview, Bitmap?>(nameof(TextureBitmap), o => o.TextureBitmap);

        private Bitmap? _textureBitmap;
        public Bitmap? TextureBitmap {
            get => _textureBitmap;
            private set {
                SetAndRaise(TextureBitmapProperty, ref _textureBitmap, value);
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

        private readonly ILogger _logger;

        public DatObjectPreview() {
            InitializeComponent();
            var loggerFactory = WorldBuilder.App.Services?.GetService<ILoggerFactory>() ?? LoggerFactory.Create(builder => {
                builder.AddProvider(new ColorConsoleLoggerProvider());
                builder.SetMinimumLevel(LogLevel.Debug);
            });
            _logger = loggerFactory.CreateLogger<DatObjectPreview>();
            
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

            // Handle palette property changes to set interpolation mode
            PropertyChanged += (s, e) => {
                if (e.Property == IsPaletteProperty || e.Property == IsPalSetProperty) {
                    var image = this.FindControl<Image>("PreviewImage");
                    if (image != null && (e.NewValue != null || e.Property == IsPalSetProperty)) {
                        var isPaletteOrPalSet = (bool)(e.NewValue ?? IsPalSet);
                        RenderOptions.SetBitmapInterpolationMode(image,
                            isPaletteOrPalSet ? BitmapInterpolationMode.None
                                             : BitmapInterpolationMode.HighQuality);
                    }
                }
            };
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
            _audioPlaybackEngine?.Dispose();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == DataIdProperty || change.Property == DatsProperty || change.Property == TargetTypeProperty) {
                UpdatePreview();
            }
            else if (change.Property == TextureBitmapProperty || change.Property == ZoomLevelProperty || change.Property == ImageStretchProperty || change.Property == ShowWireframeProperty) {
                UpdateZoomedSize();
            }
        }

        private async void UpdatePreview() {
            var updateId = ++_lastUpdateId;
            var dataId = DataId;
            var dats = Dats;

            if (dats == null || dataId == 0) {
                if (updateId == _lastUpdateId) {
                    Is3D = false;
                    Is2D = false;
                    IsSetup = false;
                    IsPreviewable = false;
                    HasData = false;
                    DataObjectType = DBObjType.Unknown;
                    PreviewDetails = null;
                    TextureBitmap = null;
                }
                return;
            }

            var resolutions = dats.ResolveId(dataId).ToList();
            if (resolutions.Count == 0) {
                if (updateId == _lastUpdateId) {
                    Is3D = false;
                    Is2D = false;
                    IsSetup = false;
                    IsPreviewable = false;
                    HasData = false;
                    DataObjectType = DBObjType.Unknown;
                    PreviewDetails = null;
                    TextureBitmap = null;
                }
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

            if (updateId != _lastUpdateId) return;

            HasData = true;
            var type = selectedResolution.Type;
            var db = selectedResolution.Database;
            DataObjectType = type;
            PreviewDetails = null;

            IsSetup = type == DBObjType.Setup || type == DBObjType.EnvCell;
            Is3D = IsSetup || type == DBObjType.GfxObj || type == DBObjType.Environment;
            Is2D = type == DBObjType.SurfaceTexture || type == DBObjType.RenderSurface || type == DBObjType.Surface || type == DBObjType.Palette || type == DBObjType.PalSet;
            IsPalette = type == DBObjType.Palette;
            IsPalSet = type == DBObjType.PalSet;
            IsWave = type == DBObjType.Wave;

            IsPreviewable = Is3D || Is2D || IsWave;

            if (Is2D) {
                var textureService = WorldBuilder.App.ProjectManager?.GetProjectService<TextureService>();
                if (textureService != null) {
                    Bitmap? bitmap = null;
                    if (DataObjectType == DBObjType.Surface) {
                        if (db.TryGet<Surface>(dataId, out var surface)) {
                            bool isClipMap = surface.Type.HasFlag(SurfaceType.Base1ClipMap);
                            if (surface.OrigTextureId != 0) {
                                bitmap = await textureService.GetTextureAsync(surface.OrigTextureId, surface.OrigPaletteId, isClipMap);
                            }
                            else if (surface.Type.HasFlag(SurfaceType.Base1Solid)) {
                                bitmap = textureService.CreateSolidColorBitmap(surface.ColorValue);
                            }
                        }
                    }
                    else if (DataObjectType == DBObjType.Palette) {
                        if (db.TryGet<Palette>(dataId, out var palette)) {
                            bitmap = textureService.CreatePaletteBitmap(palette);
                        }
                    }
                    else if (DataObjectType == DBObjType.PalSet) {
                        if (db.TryGet<PalSet>(dataId, out var palSet)) {
                            bitmap = textureService.CreatePalSetBitmap(palSet);
                        }
                    }
                    else {
                        bitmap = await textureService.GetTextureAsync(dataId);
                    }

                    if (updateId == _lastUpdateId) {
                        TextureBitmap = bitmap;
                    }
                }

                if (updateId == _lastUpdateId) {
                    if (DataObjectType == DBObjType.RenderSurface) {
                        if (db.TryGet<RenderSurface>(dataId, out var surf)) {
                            PreviewDetails = $"{surf.Width}x{surf.Height} {surf.Format}";
                        }
                    }
                    else if (DataObjectType == DBObjType.SurfaceTexture) {
                        if (db.TryGet<SurfaceTexture>(dataId, out var surfTex)) {
                            PreviewDetails = $"{surfTex.Textures.Count} textures";
                        }
                    }
                    else if (DataObjectType == DBObjType.Surface) {
                        if (db.TryGet<Surface>(dataId, out var surface)) {
                            PreviewDetails = $"{surface.Type}";
                        }
                    }
                    else if (DataObjectType == DBObjType.Palette) {
                        if (db.TryGet<Palette>(dataId, out var palette)) {
                            PreviewDetails = $"{palette.Colors.Count} colors";
                        }
                    }
                    else if (DataObjectType == DBObjType.PalSet) {
                        if (db.TryGet<PalSet>(dataId, out var palSet)) {
                            var totalColors = 0;
                            foreach (var paletteRef in palSet.Palettes) {
                                // Try to resolve palette ID using dats resolver
                                var paletteResolutions = dats.ResolveId(paletteRef.DataId).ToList();
                                foreach (var res in paletteResolutions) {
                                    if (res.Database.TryGet<Palette>(paletteRef.DataId, out var palette)) {
                                        totalColors += palette.Colors.Count;
                                        break; // Found the palette, move to next
                                    }
                                }
                            }
                            var paletteText = palSet.Palettes.Count == 1 ? "palette" : "palettes";
                            PreviewDetails = $"{palSet.Palettes.Count} {paletteText}, {totalColors} colors total";
                        }
                    }
                }
            }
            else if (DataObjectType == DBObjType.Environment) {
                if (db.TryGet<DatReaderWriter.DBObjs.Environment>(dataId, out var env)) {
                    if (env.Cells.Count > 1)
                        PreviewDetails = $"{env.Cells.Count} CellStructs";
                }
            }
            else {
                if (updateId == _lastUpdateId) {
                    TextureBitmap = null;
                }
            }
        }

        private static AudioPlaybackEngine? _audioPlaybackEngine;

        [RelayCommand]
        private void PlaySound() {
            if (!IsWave || Dats == null) return;
            try {
                var dataId = DataId;
                var resolutions = Dats.ResolveId(dataId).ToList();
                foreach (var resolution in resolutions) {
                    var db = resolution.Database;
                    if (db.TryGet<Wave>(dataId, out var wave)) {
                        // Initialize engine once (lazy initialization)
                        _audioPlaybackEngine ??= new AudioPlaybackEngine();

                        if (wave.IsMp3()) {
                            var stream = new MemoryStream(wave.Data);
                            _audioPlaybackEngine.PlaySound(stream, true);
                        }
                        else {
                            var stream = wave.ToWavStream();
                            _audioPlaybackEngine.PlaySound(stream, false);
                        }
                        break;
                    }
                }
            }
            catch (Exception ex) {
                _logger.LogError(ex, "Error playing wave {DataId:X8}", DataId);
            }
        }
    }
}
