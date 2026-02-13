using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.Enums;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using WorldBuilder.Modules.Landscape;
using WorldBuilder.Services;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Modules.Landscape.Tools;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Converters {
    public class TextureToViewModelConverter : IMultiValueConverter, IValueConverter {
        // Cache to avoid reloading the same texture view model multiple times for the same type
        private static readonly Dictionary<TerrainTextureType, TextureLoader> _cache = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) {
            return Convert(new List<object?> { value }, targetType, parameter, culture);
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) {
            throw new NotImplementedException();
        }

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) {
            if (values.Count < 1 || values[0] == Avalonia.AvaloniaProperty.UnsetValue || values[0] == null) {
                return null;
            }

            if (values[0] is TextureLoader alreadyLoader) {
                return alreadyLoader;
            }

            if (values[0] is not TerrainTextureType textureType) {
                return null;
            }

            var viewModel = (values.Count > 1 && values[1] != Avalonia.AvaloniaProperty.UnsetValue) ? values[1] : null;
            var activeDoc = (values.Count > 2 && values[2] != Avalonia.AvaloniaProperty.UnsetValue)
                ? values[2] as WorldBuilder.Shared.Models.LandscapeDocument
                : null;

            var projectManager = WorldBuilder.App.Services?.GetService<ProjectManager>();

            if (viewModel == null) {
                viewModel = projectManager?.GetProjectService<WorldBuilder.Modules.Landscape.LandscapeViewModel>();
            }

            if (activeDoc == null && viewModel is ITexturePaintingTool tool) {
                activeDoc = tool.ActiveDocument;
            }
            if (activeDoc == null && viewModel is LandscapeViewModel lvm) {
                activeDoc = lvm.ActiveDocument;
            }

            if (_cache.TryGetValue(textureType, out var loader)) {
                var region = activeDoc?.Region;
                if (loader.Image == null && region != null) {
                    loader.Reload(region);
                }
                return loader;
            }

            var textureService = projectManager?.GetProjectService<TextureService>();

            if (textureService == null) {
                return null;
            }

            var loaderRegion = activeDoc?.Region;
            loader = new TextureLoader(textureType, loaderRegion, textureService);
            _cache[textureType] = loader;
            return loader;
        }
    }

    public partial class TextureLoader : ObservableObject {
        [ObservableProperty] private Bitmap? _image;
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private TerrainTextureType _type;

        private readonly TextureService _service;

        public TextureLoader(TerrainTextureType type, ITerrainInfo? region, TextureService service) {
            Type = type;
            _service = service;
            LoadImage(type, region);
        }

        public void Reload(ITerrainInfo? region) {
            if (region != null && !IsLoading) {
                LoadImage(Type, region);
            }
        }

        private async void LoadImage(TerrainTextureType type, ITerrainInfo? region) {
            if (region == null) {
                return;
            }
            IsLoading = true;
            try {
                Image = await _service.GetTextureAsync(type, region);
            }
            catch (Exception) {
                // TODO: Log error or show fallback?
            }
            finally {
                IsLoading = false;
            }
        }
    }
}