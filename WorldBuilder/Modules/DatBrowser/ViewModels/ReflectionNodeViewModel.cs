using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using WorldBuilder.ViewModels;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Lib.IO;
using DatReaderWriter.Types;
using DatReaderWriter;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public class ReflectionNodeViewModel : ViewModelBase {
        public string Name { get; }
        public string? Value { get; }
        public string TypeName { get; }
        public ObservableCollection<ReflectionNodeViewModel>? Children { get; }

        public ReflectionNodeViewModel(string name, string? value, string typeName, IEnumerable<ReflectionNodeViewModel>? children = null) {
            Name = name;
            Value = value;
            TypeName = typeName;
            if (children != null) {
                Children = new ObservableCollection<ReflectionNodeViewModel>(children);
            }
        }

        public static ReflectionNodeViewModel Create(string name, object? obj, HashSet<object>? visited = null) {
            if (obj == null) {
                return new ReflectionNodeViewModel(name, "null", "object");
            }

            var type = obj.GetType();
            visited ??= new HashSet<object>(ReferenceEqualityComparer.Instance);

            if (IsSimpleType(type)) {
                return new ReflectionNodeViewModel(name, obj.ToString(), type.Name);
            }

            if (visited.Contains(obj)) {
                return new ReflectionNodeViewModel(name, "{Circular Reference}", type.Name);
            }

            visited.Add(obj);

            var children = new List<ReflectionNodeViewModel>();

            if (obj is IEnumerable enumerable && obj is not string) {
                int index = 0;
                foreach (var item in enumerable) {
                    children.Add(Create($"[{index++}]", item, new HashSet<object>(visited, ReferenceEqualityComparer.Instance)));
                }
            } else {
                var flags = BindingFlags.Public | BindingFlags.Instance;
                foreach (var field in type.GetFields(flags)) {
                    try {
                        children.Add(Create(field.Name, field.GetValue(obj), new HashSet<object>(visited, ReferenceEqualityComparer.Instance)));
                    } catch (Exception ex) {
                        children.Add(new ReflectionNodeViewModel(field.Name, $"Error: {ex.Message}", field.FieldType.Name));
                    }
                }
                foreach (var prop in type.GetProperties(flags)) {
                    if (prop.GetIndexParameters().Length > 0) continue;
                    try {
                        children.Add(Create(prop.Name, prop.GetValue(obj), new HashSet<object>(visited, ReferenceEqualityComparer.Instance)));
                    } catch (Exception ex) {
                        children.Add(new ReflectionNodeViewModel(prop.Name, $"Error: {ex.Message}", prop.PropertyType.Name));
                    }
                }
            }

            return new ReflectionNodeViewModel(name, null, type.Name, children.OrderBy(x => x.Name));
        }

        private static bool IsSimpleType(Type type) {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>)) {
                return IsSimpleType(Nullable.GetUnderlyingType(type)!);
            }
            return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(Guid);
        }
    }

    internal class ReferenceEqualityComparer : IEqualityComparer<object> {
        public static ReferenceEqualityComparer Instance { get; } = new ReferenceEqualityComparer();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
