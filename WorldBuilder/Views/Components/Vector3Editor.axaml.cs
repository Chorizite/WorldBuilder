using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace WorldBuilder.Views.Components;

public partial class Vector3Editor : UserControl {
    public static readonly StyledProperty<decimal?> XProperty =
        AvaloniaProperty.Register<Vector3Editor, decimal?>(nameof(X));

    public static readonly StyledProperty<decimal?> YProperty =
        AvaloniaProperty.Register<Vector3Editor, decimal?>(nameof(Y));

    public static readonly StyledProperty<decimal?> ZProperty =
        AvaloniaProperty.Register<Vector3Editor, decimal?>(nameof(Z));

    public static readonly StyledProperty<bool> IsEditableProperty =
        AvaloniaProperty.Register<Vector3Editor, bool>(nameof(IsEditable));

    public static readonly StyledProperty<string> SuffixProperty =
        AvaloniaProperty.Register<Vector3Editor, string>(nameof(Suffix));

    public decimal? X {
        get => GetValue(XProperty);
        set => SetValue(XProperty, value);
    }

    public decimal? Y {
        get => GetValue(YProperty);
        set => SetValue(YProperty, value);
    }

    public decimal? Z {
        get => GetValue(ZProperty);
        set => SetValue(ZProperty, value);
    }

    public bool IsEditable {
        get => GetValue(IsEditableProperty);
        set => SetValue(IsEditableProperty, value);
    }

    public string Suffix {
        get => GetValue(SuffixProperty);
        set => SetValue(SuffixProperty, value);
    }

    public Vector3Editor() {
        InitializeComponent();
    }
}
