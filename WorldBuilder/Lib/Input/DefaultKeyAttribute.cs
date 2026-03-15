namespace WorldBuilder.Lib.Input {
    /// <summary>
    /// Attribute to specify the default key binding for an InputAction.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class DefaultKeyAttribute : Attribute {
        public string Key { get; }
        public string Modifiers { get; }
        public int Order { get; }

        public DefaultKeyAttribute(string key, string modifiers = "", int order = 0) {
            Key = key;
            Modifiers = modifiers;
            Order = order;
        }
    }

    /// <summary>
    /// Attribute to specify the category for an InputAction.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class CategoryAttribute : Attribute {
        public string Name { get; }
        public int Order { get; }

        public CategoryAttribute(string name, int order = 0) {
            Name = name;
            Order = order;
        }
    }
}
