using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Xaml.Interactivity;
using System;

namespace WorldBuilder.Lib.Behaviors {
    public class ListBoxItemClassesBehavior : Behavior<ListBoxItem> {
        public static readonly StyledProperty<string> ClassesProperty =
            AvaloniaProperty.Register<ListBoxItemClassesBehavior, string>(nameof(Classes), defaultBindingMode: BindingMode.OneWay);

        public string Classes {
            get => GetValue(ClassesProperty);
            set => SetValue(ClassesProperty, value);
        }

        protected override void OnAttached() {
            base.OnAttached();
            UpdateClasses();
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            if (change.Property == ClassesProperty) {
                UpdateClasses();
            }
        }

        private void UpdateClasses() {
            if (AssociatedObject == null) return;
            AssociatedObject.Classes.Clear();
            if (!string.IsNullOrEmpty(Classes)) {
                foreach (var className in Classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) {
                    AssociatedObject.Classes.Add(className);
                }
            }
        }
    }
}