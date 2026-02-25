using Avalonia;
using Avalonia.Controls;
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

        public static readonly StyledProperty<Type?> TargetTypeProperty =
            AvaloniaProperty.Register<DataIdPreview, Type?>(nameof(TargetType));

        public Type? TargetType {
            get => GetValue(TargetTypeProperty);
            set => SetValue(TargetTypeProperty, value);
        }

        public DataIdPreview() {
            InitializeComponent();
        }
    }
}
