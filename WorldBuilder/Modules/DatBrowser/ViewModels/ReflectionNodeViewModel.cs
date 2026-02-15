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

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public record OpenQualifiedDataIdMessage(uint DataId, Type? TargetType);

    public partial class ReflectionNodeViewModel : ViewModelBase {
        public string Name { get; }
        public string? Value { get; }
        public string TypeName { get; }
        public ObservableCollection<ReflectionNodeViewModel>? Children { get; }

        public uint? DataId { get; set; }
        public Type? TargetType { get; set; }
        public IDatReaderWriter? Dats { get; set; }
        public bool IsPreviewable => IsQualifiedDataId && (DbType == DBObjType.Setup || DbType == DBObjType.GfxObj || DbType == DBObjType.SurfaceTexture || DbType == DBObjType.RenderSurface);
        public bool IsQualifiedDataId => DataId.HasValue;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsPreviewable))]
        private DBObjType? _dbType;

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

        public static ReflectionNodeViewModel Create(string name, object? obj, IDatReaderWriter dats, HashSet<object>? visited = null, int depth = 0) {
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

            if (obj is byte[] bytes) {
                return new ReflectionNodeViewModel(name, "byte[]", $"{bytes.Length} bytes");
            }

            if (obj is QualifiedDataId qid) {
                var node = new ReflectionNodeViewModel(name, $"0x{qid.DataId:X8}", type.Name);
                node.DataId = qid.DataId;
                node.Dats = dats;
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(QualifiedDataId<>)) {
                    node.TargetType = type.GetGenericArguments()[0];
                }

                if (node.DataId.HasValue) {
                    node.DbType = dats.TypeFromId(node.DataId.Value);
                }

                return node;
            }

            if (visited.Contains(obj)) {
                return new ReflectionNodeViewModel(name, "{Circular Reference}", type.Name);
            }

            visited.Add(obj);

            var children = new List<ReflectionNodeViewModel>();
            bool isList = false;

            if (obj is IDictionary dictionary) {
                foreach (DictionaryEntry entry in dictionary) {
                    children.Add(Create(entry.Key?.ToString() ?? "null", entry.Value, dats, new HashSet<object>(visited, ReferenceEqualityComparer.Instance), depth + 1));
                }
            } else if (obj is IEnumerable enumerable && obj is not string) {
                int index = 0;
                foreach (var item in enumerable) {
                    var itemType = item?.GetType();
                    if (itemType != null && itemType.IsGenericType && itemType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)) {
                        var key = itemType.GetProperty("Key")?.GetValue(item);
                        var value = itemType.GetProperty("Value")?.GetValue(item);
                        children.Add(Create(key?.ToString() ?? "null", value, dats, new HashSet<object>(visited, ReferenceEqualityComparer.Instance), depth + 1));
                    } else {
                        isList = true;
                        children.Add(Create($"[{index++}]", item, dats, new HashSet<object>(visited, ReferenceEqualityComparer.Instance), depth + 1));
                    }
                }
            } else {
                var flags = BindingFlags.Public | BindingFlags.Instance;
                foreach (var field in type.GetFields(flags)) {
                    try {
                        children.Add(Create(field.Name, field.GetValue(obj), dats, new HashSet<object>(visited, ReferenceEqualityComparer.Instance), depth + 1));
                    } catch (Exception ex) {
                        children.Add(new ReflectionNodeViewModel(field.Name, $"Error: {ex.Message}", field.FieldType.Name));
                    }
                }
                foreach (var prop in type.GetProperties(flags)) {
                    if (prop.GetIndexParameters().Length > 0) continue;
                    try {
                        children.Add(Create(prop.Name, prop.GetValue(obj), dats, new HashSet<object>(visited, ReferenceEqualityComparer.Instance), depth + 1));
                    } catch (Exception ex) {
                        children.Add(new ReflectionNodeViewModel(prop.Name, $"Error: {ex.Message}", prop.PropertyType.Name));
                    }
                }
            }

            return new ReflectionNodeViewModel(name, null, type.Name, isList ? children : children.OrderBy(x => x.Name));
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
