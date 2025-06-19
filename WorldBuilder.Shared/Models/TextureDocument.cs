using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.Enums;
using YDotNet.Document.Cells;

namespace WorldBuilder.Shared.Models {
    public class TextureDocument : BaseDocument {
        private YDotNet.Document.Types.Maps.Map _map;
        private uint _width;
        private uint _height;
        private PixelFormat _format;
        private byte[] _data = Array.Empty<byte>();

        public YDotNet.Document.Types.Maps.Map Map {
            get => _map;
            private set => SetProperty(ref _map, value);
        }

        public uint Width {
            get => _width;
            set {
                if (SetProperty(ref _width, value)) {
                    UpdateTextureProperty("width", Input.Long((long)value));
                }
            }
        }

        public uint Height {
            get => _height;
            set {
                if (SetProperty(ref _height, value)) {
                    UpdateTextureProperty("height", Input.Long((long)value));
                }
            }
        }

        public PixelFormat Format {
            get => _format;
            set {
                if (SetProperty(ref _format, value)) {
                    UpdateTextureProperty("format", Input.Long((long)value));
                }
            }
        }

        public byte[] Data {
            get => _data;
            set {
                if (SetProperty(ref _data, value ?? Array.Empty<byte>())) {
                    UpdateTextureProperty("data", Input.Bytes(_data));
                }
            }
        }

        public long DataSize => Data?.Length ?? 0;

        protected override void InitializeDocument() {
            Map = Doc.Map("texture");
            RefreshProperties();
        }

        protected override void OnDocumentUpdated() {
            RefreshProperties();
        }

        private void RefreshProperties() {
            lock (docLock) {
                using var readTransaction = Doc.ReadTransaction();
                var widthCell = Map.Get(readTransaction, "width");
                var heightCell = Map.Get(readTransaction, "height");
                var dataCell = Map.Get(readTransaction, "data");

                _width = widthCell != null ? (uint)Math.Max(0, widthCell.Long) : 0;
                _height = heightCell != null ? (uint)Math.Max(0, heightCell.Long) : 0;
                _data = dataCell?.Bytes ?? Array.Empty<byte>();

                OnPropertyChanged(nameof(Width));
                OnPropertyChanged(nameof(Height));
                OnPropertyChanged(nameof(Data));
                OnPropertyChanged(nameof(DataSize));
            }
        }

        private void UpdateTextureProperty(string key, Input value) {
            if (!IsInitialized) return;

            lock (docLock) {
                using var writeTransaction = Doc.WriteTransaction();
                Map.Insert(writeTransaction, key, value);
                writeTransaction.Commit();
            }
        }

        public async void SetTexture(uint newWidth, uint newHeight, PixelFormat format, byte[] newData) {
            if (!IsInitialized) return;

            var timestamp = DateTime.UtcNow;

            lock (docLock) {
                using var writeTransaction = Doc.WriteTransaction();
                Map.Insert(writeTransaction, "width", Input.Long((long)newWidth));
                Map.Insert(writeTransaction, "height", Input.Long((long)newHeight));
                Map.Insert(writeTransaction, "format", Input.Long((long)format));
                Map.Insert(writeTransaction, "data", Input.Bytes(newData ?? Array.Empty<byte>()));
                writeTransaction.Commit();
            }

            // Update local properties
            _width = newWidth;
            _height = newHeight;
            _format = format;
            _data = newData ?? Array.Empty<byte>();

            OnPropertyChanged(nameof(Width));
            OnPropertyChanged(nameof(Height));
            OnPropertyChanged(nameof(Data));
            OnPropertyChanged(nameof(DataSize));
        }
    }
}
