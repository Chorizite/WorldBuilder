using Avalonia.Headless.XUnit;
using System;
using System.Collections.Generic;
using System.Reflection;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Shared.Lib.Settings;
using Xunit;

namespace WorldBuilder.Tests.Services {
    public class SettingsUIGeneratorTests {
        [AvaloniaFact]
        public void FindInstance_SkipsIndexedProperties() {
            var root = new RootSettings();
            var generator = new SettingsUIGenerator(root);
            
            // This should not throw TargetParameterCountException
            var panel = generator.GenerateContentPanels();
            
            Assert.NotNull(panel);
        }

        public class RootSettings {
            public SubSettings Sub { get; set; } = new();
            public List<string> SomeList { get; set; } = new() { "test" };
        }

        [SettingCategory("Sub")]
        public class SubSettings {
            public bool Value { get; set; }
        }
    }
}
