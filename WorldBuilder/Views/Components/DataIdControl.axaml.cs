using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DatReaderWriter.Enums;
using WorldBuilder.Shared.Services;
using WorldBuilder.Modules.DatBrowser.ViewModels;

namespace WorldBuilder.Views.Components {
    public partial class DataIdControl : UserControl {
        public static readonly StyledProperty<uint> DataIdProperty =
            AvaloniaProperty.Register<DataIdControl, uint>(nameof(DataId));

        public uint DataId {
            get => GetValue(DataIdProperty);
            set => SetValue(DataIdProperty, value);
        }

        public static readonly StyledProperty<IDatReaderWriter?> DatsProperty =
            AvaloniaProperty.Register<DataIdControl, IDatReaderWriter?>(nameof(Dats));

        public IDatReaderWriter? Dats {
            get => GetValue(DatsProperty);
            set => SetValue(DatsProperty, value);
        }

        public static readonly StyledProperty<DBObjType?> DbTypeProperty =
            AvaloniaProperty.Register<DataIdControl, DBObjType?>(nameof(DbType));

        public DBObjType? DbType {
            get => GetValue(DbTypeProperty);
            set => SetValue(DbTypeProperty, value);
        }

        public IRelayCommand OpenInNewWindowCommand { get; }

        public DataIdControl() {
            OpenInNewWindowCommand = new RelayCommand(OpenInNewWindow);
            InitializeComponent();
        }

        private void OpenInNewWindow() {
            if (DataId != 0) {
                WeakReferenceMessenger.Default.Send(new OpenQualifiedDataIdMessage(DataId, null));
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);

            if (change.Property == DataIdProperty || change.Property == DatsProperty) {
                UpdateDbType();
            }
        }

        private void UpdateDbType() {
            if (Dats != null && DataId != 0) {
                DbType = Dats.TypeFromId(DataId);
                if (DbType == DBObjType.Unknown) {
                    DbType = null;
                }
            }
            else {
                DbType = null;
            }
        }
    }
}
