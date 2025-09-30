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

            var name = data.GetType().FullName!.Replace("ViewModel", "View");
            var type = Type.GetType(name);

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
