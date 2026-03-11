using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Types;
using System.Collections;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public record OpenQualifiedDataIdMessage(uint DataId, Type? TargetType);

    public partial class ReflectionNodeViewModel : ViewModelBase {
        public string Name { get; set; }
        public string? Value { get; set; }
        public string TypeName { get; }
        public ObservableCollection<ReflectionNodeViewModel>? Children { get; }

        public uint? DataId { get; set; }
        public Type? TargetType { get; set; }
        public IDatReaderWriter? Dats { get; set; }
        public bool IsPreviewable => IsQualifiedDataId && (DbType == DBObjType.Setup || DbType == DBObjType.GfxObj || DbType == DBObjType.SurfaceTexture || DbType == DBObjType.RenderSurface);
        public bool IsQualifiedDataId => DataId.HasValue;

        private static Dictionary<ushort, MotionCommand> _rawToInterpreted;

        [UnconditionalSuppressMessage("AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling.", Justification = "<Pending>")]
        static ReflectionNodeViewModel() {
            _rawToInterpreted = new Dictionary<ushort, MotionCommand>();
            var interpretedCommands = Enum.GetValues(typeof(MotionCommand));
            foreach (var interpretedCommand in interpretedCommands)
                _rawToInterpreted.Add((ushort)(uint)interpretedCommand, (MotionCommand)interpretedCommand);
        }
        
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsPreviewable))]
        private DBObjType? _dbType;

        [RelayCommand]
        private void Copy() {
            if (DataId.HasValue) {
                TopLevel.Clipboard?.SetTextAsync($"0x{DataId.Value:X8}");
            }
            else {
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

        /// <summary>
        /// Creates a ReflectionNodeViewModel from a DataId, resolving its type from the dat databases.
        /// </summary>
        public static ReflectionNodeViewModel CreateFromDataId(string name, uint dataId, IDatReaderWriter dats) {
            var resolutions = dats.ResolveId(dataId).ToList();
            var type = resolutions.FirstOrDefault()?.Type ?? DBObjType.Unknown;
            var node = new ReflectionNodeViewModel(name, $"0x{dataId:X8}", type.ToString());
            node.DataId = dataId;
            node.Dats = dats;
            node.DbType = type;
            return node;
        }

        [UnconditionalSuppressMessage("Trimming", "IL2075:Reflection is used for debugging/browsing", Justification = "This is a developer tool for browsing object graphs")]
        public static ReflectionNodeViewModel Create(string name, object? obj, IDatReaderWriter dats, HashSet<object>? visited = null, int depth = 0, List<Type>? typeChain = null) {
            if (obj == null) {
                return new ReflectionNodeViewModel(name, "null", "object");
            }

            if (depth > 10) {
                return new ReflectionNodeViewModel(name, "{Max Depth Reached}", obj.GetType().Name);
            }

            var type = obj.GetType();
            visited ??= new HashSet<object>(ReferenceEqualityComparer.Instance);

            if (IsSimpleType(type)) {
                var value = ProcessTypeValue(name, obj, typeChain);
                return new ReflectionNodeViewModel(name, value, type.Name);
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
                    var resolutions = dats.ResolveId(node.DataId.Value).ToList();
                    if (resolutions.Count > 0) {
                        node.DbType = resolutions.First().Type;
                    }
                }

                return node;
            }

            if (obj is PackedQualifiedDataId pqid) {
                var node = new ReflectionNodeViewModel(name, $"0x{pqid.DataId:X8}", type.Name);
                node.DataId = pqid.DataId;
                node.Dats = dats;
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(PackedQualifiedDataId<>)) {
                    node.TargetType = type.GetGenericArguments()[0];
                }

                if (node.DataId.HasValue) {
                    var resolutions = dats.ResolveId(node.DataId.Value).ToList();
                    if (resolutions.Count > 0) {
                        node.DbType = resolutions.First().Type;
                    }
                }

                return node;
            }

            if (visited.Contains(obj)) {
                return new ReflectionNodeViewModel(name, "{Circular Reference}", type.Name);
            }

            visited.Add(obj);

            typeChain ??= new List<Type>();
            typeChain.Add(type);

            var children = new List<ReflectionNodeViewModel>();
            bool isList = false;
            Type? keyType = typeof(string);
            
            if (obj is IDictionary dictionary) {
                if (dictionary.Count > 0) {
                    keyType = dictionary.Keys.Cast<object>().First()?.GetType() ?? typeof(string);
                }
                foreach (DictionaryEntry entry in dictionary) {
                    children.Add(Create(entry.Key?.ToString() ?? "null", entry.Value, dats, new HashSet<object>(visited, ReferenceEqualityComparer.Instance), depth + 1, typeChain));
                }
                ProcessTypeNaming(name, children, typeChain);
            }
            else if (obj is IEnumerable enumerable && obj is not string) {
                int index = 0;
                foreach (var item in enumerable) {
                    var itemType = item?.GetType();
                    if (itemType != null && itemType.IsGenericType && itemType.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)) {
                        var key = itemType.GetProperty("Key")?.GetValue(item);
                        var value = itemType.GetProperty("Value")?.GetValue(item);
                        children.Add(Create(key?.ToString() ?? "null", value, dats, new HashSet<object>(visited, ReferenceEqualityComparer.Instance), depth + 1, typeChain));
                    }
                    else {
                        isList = true;
                        children.Add(Create($"[{index++}]", item, dats, new HashSet<object>(visited, ReferenceEqualityComparer.Instance), depth + 1, typeChain));
                    }
                }
                ProcessTypeNaming(name, children, typeChain);
            }
            else {
                var flags = BindingFlags.Public | BindingFlags.Instance;
                foreach (var field in type.GetFields(flags)) {
                    try {
                        var childNode = Create(field.Name, field.GetValue(obj), dats, new HashSet<object>(visited, ReferenceEqualityComparer.Instance), depth + 1, typeChain);
                        children.Add(childNode);
                    }
                    catch (Exception ex) {
                        children.Add(new ReflectionNodeViewModel(field.Name, $"Error: {ex.Message}", field.FieldType.Name));
                    }
                }
                foreach (var prop in type.GetProperties(flags)) {
                    if (prop.GetIndexParameters().Length > 0) continue;
                    try {
                        children.Add(Create(prop.Name, prop.GetValue(obj), dats, new HashSet<object>(visited, ReferenceEqualityComparer.Instance), depth + 1, typeChain));
                    }
                    catch (Exception ex) {
                        children.Add(new ReflectionNodeViewModel(prop.Name, $"Error: {ex.Message}", prop.PropertyType.Name));
                    }
                }
            }

            var result = new ReflectionNodeViewModel(name, null, type.Name, isList ? children : children.OrderBy(x => x.Name, new TypeComparer(keyType)));
            typeChain.RemoveAt(typeChain.Count - 1);
            return result;
        }

        private static bool IsSimpleType(Type type) {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>)) {
                return IsSimpleType(Nullable.GetUnderlyingType(type)!);
            }
            return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(Guid);
        }

        private static void ProcessTypeNaming(string name, List<ReflectionNodeViewModel> children, List<Type> typeChain) {

            if (children == null) return;

            var rootType = typeChain[0];
            var parentType = typeChain[^2];

            // would be nice to express this more declaratively instead of imperatively
            if (rootType == typeof(MotionTable)) {
                if (name == "Cycles" || name == "Links" || name == "Modifiers" || parentType.Equals(typeof(MotionCommandData))) {
                    foreach (var child in children) {
                        if (uint.TryParse(child.Name, out var intName)) {
                            var stanceKey = (ushort)(intName >> 16);
                            var motionKey = (ushort)intName;

                            if (_rawToInterpreted.TryGetValue(stanceKey, out var stance) && _rawToInterpreted.TryGetValue(motionKey, out var motion))
                                child.Name = $"{stance} - {motion}";
                            else if (Enum.IsDefined(typeof(MotionCommand), intName))
                                child.Name = $"{(MotionCommand)intName}";
                            else
                                child.Name = $"{intName:X8}";
                        }
                    }
                }
            }
            else if (rootType == typeof(MasterInputMap)) {
                if (name == "InputMaps") {
                    foreach (var child in children) {
                        if (uint.TryParse(child.Name, out var intName))
                            child.Name = $"0x{intName:X8}";
                    }
                }
            }
            else if (rootType == typeof(MaterialInstance)) {
                if (name == "ModifierRefs") {
                    foreach (var child in children) {
                        if (uint.TryParse(child.Value, out var intValue))
                            child.Value = $"0x{intValue:X8}";
                    }
                }
            }
            else if (rootType == typeof(EnumIDMap)) {
                if (name == "ClientEnumToID" || name == "ServerEnumToID") {
                    foreach (var child in children) {
                        if (uint.TryParse(child.Name, out var intName))
                            child.Name = $"0x{intName:X8}";
                        if (uint.TryParse(child.Value, out var intValue))
                            child.Value = $"0x{intValue:X8}";
                    }
                }
                else if (name == "ClientEnumToName" || name == "ServerEnumToName") {
                    foreach (var child in children) {
                        if (uint.TryParse(child.Name, out var intName))
                            child.Name = $"0x{intName:X8}";
                    }
                }
            }
            else if (rootType == typeof(MasterProperty)) {
                if (name == "Properties") {
                    foreach (var child in children) {
                        if (uint.TryParse(child.Name, out var intName))
                            child.Name = $"0x{intName:X8}";
                    }
                }
            }
            else if (rootType == typeof(CharGen)) {
                if (name == "EyeColors" || name == "HairColors") {
                    foreach (var child in children) {
                        if (uint.TryParse(child.Value, out var intValue))
                            child.Value = $"0x{intValue:X8}";
                    }
                }
            }
        }

        private static string? ProcessTypeValue(string name, object obj, List<Type>? typeChain) {
           
            var type = obj.GetType();
            var value = obj.ToString();
            if (type == typeof(uint) && name.ToLower().EndsWith("id")) {
                return $"0x{Convert.ToUInt32(obj):X8}";
            }
            if (typeChain?.Count > 0) {
                var rootType = typeChain[0];

                if (rootType == typeof(MasterInputMap)) {
                    if (name == "Key" || name == "Modifier" || name == "Unknown") {
                        if (uint.TryParse(value, out var intValue))
                            return $"0x{intValue:X8}";
                    }
                }
                else if (rootType == typeof(EnumMapper)) {
                    if (name == "BaseEnumMap") {
                        if (uint.TryParse(value, out var intValue))
                            return $"0x{intValue:X8}";
                    }
                }
                else if (rootType == typeof(MasterProperty)) {
                    if (type == typeof(uint) && (name == "Name" || name == "Data")) {
                        return $"0x{Convert.ToUInt32(obj):X8}";
                    }
                }
                else if (rootType == typeof(SpellComponentTable)) {
                    if (name == "Gesture") {
                        if (uint.TryParse(value, out var intValue))
                            return $"0x{intValue:X8}";
                    }
                }
            }
            return value;
        }
    }

    internal class ReferenceEqualityComparer : IEqualityComparer<object> {
        public static ReferenceEqualityComparer Instance { get; } = new ReferenceEqualityComparer();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    internal class TypeComparer : IComparer<string> {
        private readonly Type? _keyType;

        public TypeComparer(Type? keyType) {
            _keyType = keyType;
        }

        public int Compare(string? x, string? y) {
            if (_keyType == typeof(int) || _keyType == typeof(short)) {
                if (int.TryParse(x, out var xNum) && int.TryParse(y, out var yNum)) {
                    return xNum.CompareTo(yNum);
                }
            }
            else if (_keyType == typeof(uint) || _keyType == typeof(ushort)) {
                if (uint.TryParse(x, out var xNum) && uint.TryParse(y, out var yNum)) {
                    return xNum.CompareTo(yNum);
                }
            }
            return string.Compare(x, y);
        }
    }
}
