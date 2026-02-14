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

using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public record OpenQualifiedDataIdMessage(uint DataId, Type? TargetType);

    public partial class ReflectionNodeViewModel : ViewModelBase {
        public string Name { get; }
        public string? Value { get; }
        public string TypeName { get; }
        public ObservableCollection<ReflectionNodeViewModel>? Children { get; }

        public uint? DataId { get; set; }
        public Type? TargetType { get; set; }
        public bool IsQualifiedDataId => DataId.HasValue;

        [RelayCommand]
        private void Copy() {
            if (DataId.HasValue) {
                TopLevel.Clipboard?.SetTextAsync($"0x{DataId.Value:X8}");
            } else {
                TopLevel.Clipboard?.SetTextAsync(Value ?? "");
            }
        }

        [RelayCommand]
        private void OpenInNewWindow() {
            if (DataId.HasValue) {
                WeakReferenceMessenger.Default.Send(new OpenQualifiedDataIdMessage(DataId.Value, TargetType));
            }
        }

        public ReflectionNodeViewModel(string name, string? value, string typeName, IEnumerable<ReflectionNodeViewModel>? children = null) {
            Name = name;
            Value = value;
            TypeName = typeName;
            if (children != null) {
                Children = new ObservableCollection<ReflectionNodeViewModel>(children);
            }
        }

        public static ReflectionNodeViewModel Create(string name, object? obj, HashSet<object>? visited = null, int depth = 0) {
            if (obj == null) {
                return new ReflectionNodeViewModel(name, "null", "object");
            }

            if (depth > 10) {
                return new ReflectionNodeViewModel(name, "{Max Depth Reached}", obj.GetType().Name);
            }

            var type = obj.GetType();
            visited ??= new HashSet<object>(ReferenceEqualityComparer.Instance);

            if (IsSimpleType(type)) {
                return new ReflectionNodeViewModel(name, obj.ToString(), type.Name);
            }

            if (obj is QualifiedDataId qid) {
                var node = new ReflectionNodeViewModel(name, qid.ToString(), type.Name);
                node.DataId = qid.DataId;
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(QualifiedDataId<>)) {
                    node.TargetType = type.GetGenericArguments()[0];
                }
                return node;
            }

            if (visited.Contains(obj)) {
                return new ReflectionNodeViewModel(name, "{Circular Reference}", type.Name);
            }

            visited.Add(obj);

            var children = new List<ReflectionNodeViewModel>();

            if (obj is IEnumerable enumerable && obj is not string) {
                int index = 0;
                foreach (var item in enumerable) {
                    children.Add(Create($"[{index++}]", item, new HashSet<object>(visited, ReferenceEqualityComparer.Instance), depth + 1));
                }
            } else {
                var flags = BindingFlags.Public | BindingFlags.Instance;
                foreach (var field in type.GetFields(flags)) {
                    try {
                        children.Add(Create(field.Name, field.GetValue(obj), new HashSet<object>(visited, ReferenceEqualityComparer.Instance), depth + 1));
                    } catch (Exception ex) {
                        children.Add(new ReflectionNodeViewModel(field.Name, $"Error: {ex.Message}", field.FieldType.Name));
                    }
                }
                foreach (var prop in type.GetProperties(flags)) {
                    if (prop.GetIndexParameters().Length > 0) continue;
                    try {
                        children.Add(Create(prop.Name, prop.GetValue(obj), new HashSet<object>(visited, ReferenceEqualityComparer.Instance), depth + 1));
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
