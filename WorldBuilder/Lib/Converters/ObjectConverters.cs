using Avalonia.Data.Converters;

namespace WorldBuilder.Lib.Converters {
    public static class ObjectConverters {
        public static readonly IValueConverter IsNotNull = new FuncValueConverter<object, bool>(obj => obj != null);
    }
}