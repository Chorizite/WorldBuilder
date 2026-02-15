using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace WorldBuilder.Converters {
    public static class NumericConverters {
        public static readonly IValueConverter IsZero = new FuncValueConverter<object, bool>(value => {
            if (value is uint u) return u == 0;
            if (value is int i) return i == 0;
            return value == null;
        });

        public static readonly IValueConverter IsNotZero = new FuncValueConverter<object, bool>(value => {
            if (value is uint u) return u != 0;
            if (value is int i) return i != 0;
            return value != null;
        });
    }
}
