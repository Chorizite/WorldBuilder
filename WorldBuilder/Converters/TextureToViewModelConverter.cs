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
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Converters {
    public class TextureToViewModelConverter : IMultiValueConverter {
        // Cache to avoid reloading the same texture view model multiple times for the same type
        private static readonly Dictionary<TerrainTextureType, TextureLoader> _cache = new();

        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) {
            // Handle UnsetValue which Avalonia often passes during initialization or when bindings are broken
            if (values.Count < 2 || values[0] == Avalonia.AvaloniaProperty.UnsetValue || values[1] == Avalonia.AvaloniaProperty.UnsetValue) {
                return null;
            }

            var textureType = values[0] as TerrainTextureType?;
            var viewModel = values[1] as LandscapeViewModel;
            var activeDoc = (values.Count > 2 && values[2] != Avalonia.AvaloniaProperty.UnsetValue)
                ? values[2] as WorldBuilder.Shared.Models.LandscapeDocument
                : null;

            if (textureType == null || viewModel == null) {
                return null;
            }

            if (_cache.TryGetValue(textureType.Value, out var loader)) {
                var region = activeDoc?.Region ?? viewModel.ActiveDocument?.Region;
                if (loader.Image == null && region != null) {
                    loader.Reload(region);
                }
                return loader;
            }

            var projectManager = WorldBuilder.App.Services?.GetService<ProjectManager>();
            var textureService = projectManager?.GetProjectService<TextureService>();

            if (textureService == null) {
                return null;
            }

            loader = new TextureLoader(textureType.Value, activeDoc?.Region ?? viewModel.ActiveDocument?.Region, textureService);
            _cache[textureType.Value] = loader;
            return loader;
        }
    }

    public partial class TextureLoader : ObservableObject {
        [ObservableProperty] private Bitmap? _image;
        [ObservableProperty] private bool _isLoading;

        private readonly TerrainTextureType _type;
        private readonly TextureService _service;

        public TextureLoader(TerrainTextureType type, ITerrainInfo? region, TextureService service) {
            _type = type;
            _service = service;
            LoadImage(type, region);
        }

        public void Reload(ITerrainInfo? region) {
            if (region != null && !IsLoading) {
                LoadImage(_type, region);
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