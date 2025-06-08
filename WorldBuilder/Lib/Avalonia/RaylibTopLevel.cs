using Avalonia.Controls;
using Avalonia.Controls.Embedding;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;

namespace WorldBuilder.Lib.Avalonia;

public sealed class RaylibTopLevel : EmbeddableControlRoot {
    internal RaylibTopLevelImpl Impl { get; }


    internal RaylibTopLevel(RaylibTopLevelImpl impl)
        : base(impl) {
        Impl = impl;
    }
}
