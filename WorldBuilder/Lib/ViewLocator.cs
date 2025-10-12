using Avalonia.Controls;
using Avalonia.Controls.Templates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WorldBuilder.ViewModels;

namespace WorldBuilder.Lib {
    public class ViewLocator : IDataTemplate {
        public Control Build(object? data) {
            if (data is null) {
                return new TextBlock { Text = "data was null" };
            }

            var name = data.GetType().FullName?.Replace("ViewModel", "View")
                ?? throw new InvalidOperationException($"{data.GetType().FullName} is not a ViewModel");
#pragma warning disable IL2057 // Unrecognized value passed to the parameter of method. It's not possible to guarantee the availability of the target type.
            var type = Type.GetType(name);
#pragma warning restore IL2057 // Unrecognized value passed to the parameter of method. It's not possible to guarantee the availability of the target type.

            Console.WriteLine($"Request: {data.GetType().FullName} -> {name}");

            if (type == null) {
                return new TextBlock { Text = "Not Found: " + name };
            }

            var control = ProjectManager.Instance?.GetProjectService<Control>(type);
            if (control != null) {
                return (Control)control!;
            }

            control = App.Services?.GetService(type) as Control;

            if (control != null) {
                return (Control)control!;
            }

            control = Activator.CreateInstance(type) as Control;

            if (control != null) {
                return (Control)control!;
            }

            return new TextBlock { Text = $"Not Found: {type.Name} {name}" };
        }

        public bool Match(object? data) {
            return data is ViewModelBase;
        }
    }
}
