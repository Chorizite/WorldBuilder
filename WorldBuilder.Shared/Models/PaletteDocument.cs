using CommunityToolkit.Mvvm.ComponentModel;
using DatReaderWriter.Enums;
using YDotNet.Document.Cells;

namespace WorldBuilder.Shared.Models {
    public class PaletteDocument : BaseDocument {
        private YDotNet.Document.Types.Arrays.Array _colors;
        private byte[] _data = Array.Empty<byte>();

        public YDotNet.Document.Types.Arrays.Array Colors {
            get => _colors;
            private set => SetProperty(ref _colors, value);
        }

        public byte[] Data {
            get => _data;
            set {
                if (SetProperty(ref _data, value ?? Array.Empty<byte>())) {
                    //UpdateTextureProperty("data", Input.Bytes(_data));
                }
            }
        }

        public long DataSize => Data?.Length ?? 0;

        protected override void InitializeDocument() {
            RefreshProperties();
        }

        protected override void OnDocumentUpdated() {
            RefreshProperties();
        }

        private void RefreshProperties() {
            lock (docLock) {
            }
        }
    }
}
