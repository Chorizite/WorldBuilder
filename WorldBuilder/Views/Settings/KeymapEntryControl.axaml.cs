using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using WorldBuilder.Lib.Input;

namespace WorldBuilder.Views.Settings {
    public partial class KeymapEntryControl : UserControl {
        private InputManager? _inputManager;
        private bool _isCaptureMode = false;
        
        public static readonly StyledProperty<InputAction> ActionProperty =
            AvaloniaProperty.Register<KeymapEntryControl, InputAction>(nameof(Action));

        public static readonly StyledProperty<string> ActionNameProperty =
            AvaloniaProperty.Register<KeymapEntryControl, string>(nameof(ActionName));

        public static readonly StyledProperty<string> CurrentKeyDisplayProperty =
            AvaloniaProperty.Register<KeymapEntryControl, string>(nameof(CurrentKeyDisplay));

        public static readonly StyledProperty<string> DefaultKeyDisplayProperty =
            AvaloniaProperty.Register<KeymapEntryControl, string>(nameof(DefaultKeyDisplay));

        public static readonly StyledProperty<string> CaptureButtonTextProperty =
            AvaloniaProperty.Register<KeymapEntryControl, string>(nameof(CaptureButtonText));

        public static readonly StyledProperty<bool> HasBindingProperty =
            AvaloniaProperty.Register<KeymapEntryControl, bool>(nameof(HasBinding));

        public InputAction Action {
            get => GetValue(ActionProperty);
            set => SetValue(ActionProperty, value);
        }

        public string ActionName {
            get => GetValue(ActionNameProperty);
            set => SetValue(ActionNameProperty, value);
        }

        public string CurrentKeyDisplay {
            get => GetValue(CurrentKeyDisplayProperty);
            set => SetValue(CurrentKeyDisplayProperty, value);
        }

        public string DefaultKeyDisplay {
            get => GetValue(DefaultKeyDisplayProperty);
            set => SetValue(DefaultKeyDisplayProperty, value);
        }

        public string CaptureButtonText {
            get => GetValue(CaptureButtonTextProperty);
            set => SetValue(CaptureButtonTextProperty, value);
        }

        public bool HasBinding {
            get => GetValue(HasBindingProperty);
            set => SetValue(HasBindingProperty, value);
        }

        public KeymapEntryControl() {
            InitializeComponent();
            
            // Get InputManager from service provider
            _inputManager = WorldBuilder.App.Services?.GetService<InputManager>()
                ?? throw new InvalidOperationException("InputManager not available");

            // Set DataContext to self for XAML bindings
            DataContext = this;

            // Handle keyboard events on the Border when in capture mode
            KeyDisplayBorder.KeyDown += OnKeyDown;
            KeyDisplayBorder.KeyUp += OnKeyUp;
            KeyDisplayBorder.GotFocus += OnGotFocus;
            KeyDisplayBorder.LostFocus += OnLostFocus;
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change) {
            base.OnPropertyChanged(change);
            
            if (change.Property == ActionProperty) {
                UpdateDisplay();
            }
        }

        public void UpdateDisplay() {
            if (_inputManager == null) return;
            
            if (Action == InputAction.None) return;

            var currentBinding = _inputManager.GetKeyBinding(Action);
            
            CurrentKeyDisplay = FormatKeyBinding(currentBinding);
            HasBinding = !string.IsNullOrEmpty(currentBinding.Key);

            var defaultBinding = GetDefaultBinding(Action);
            DefaultKeyDisplay = $"Default: {FormatKeyBinding(defaultBinding)}";

            CaptureButtonText = _isCaptureMode ? "Press key..." : "Change";
        }

        private string FormatKeyBinding(WorldBuilder.Shared.Models.KeyBinding binding) {
            if (string.IsNullOrEmpty(binding.Key)) return string.Empty;
            
            if (string.IsNullOrEmpty(binding.Modifiers)) return binding.Key;
            
            // Convert comma-separated with spaces to display format with spaces around +
            var formattedModifiers = binding.Modifiers.Replace(", ", " + ");
            // Replace "Control" with "Ctrl" for display only
            formattedModifiers = formattedModifiers.Replace("Control", "Ctrl");
            return $"{formattedModifiers} + {binding.Key}";
        }

        private WorldBuilder.Shared.Models.KeyBinding GetDefaultBinding(InputAction action) {
            var field = typeof(InputAction).GetField(action.ToString());
            var attr = field?.GetCustomAttributes(typeof(DefaultKeyAttribute), false)
                .Cast<DefaultKeyAttribute>().FirstOrDefault();
            
            if (attr != null) {
                return new WorldBuilder.Shared.Models.KeyBinding(attr.Key, attr.Modifiers);
            }
            
            return new WorldBuilder.Shared.Models.KeyBinding("", "");
        }

        private void OnKeyDisplayBorderPointerPressed(object? sender, PointerPressedEventArgs e) {
            if (_inputManager == null) return;
            
            if (_isCaptureMode) {
                ExitCaptureMode();
            } else {
                EnterCaptureMode();
            }
        }

        private void OnUnbindButtonClick(object? sender, RoutedEventArgs e) {
            if (_inputManager == null) return;
            
            _inputManager.SetKeyBinding(Action, "", "");
            
            // Update display directly
            CurrentKeyDisplay = string.Empty;
            HasBinding = false;
            KeyDisplayTextBlock.Text = string.Empty;
        }

        private void EnterCaptureMode() {
            _isCaptureMode = true;
            KeyDisplayBorder.Classes.Add("capture-mode");
            KeyDisplayTextBlock.Classes.Add("capture-mode");
            
            KeyDisplayTextBlock.Text = DefaultKeyDisplay;
            
            KeyDisplayBorder.Focus();
        }

        private void ExitCaptureMode() {
            _isCaptureMode = false;
            KeyDisplayBorder.Classes.Remove("capture-mode");
            KeyDisplayTextBlock.Classes.Remove("capture-mode");
            
            // Restore original content by getting the current binding again
            if (_inputManager != null && Action != InputAction.None) {
                var currentBinding = _inputManager.GetKeyBinding(Action);
                KeyDisplayTextBlock.Text = FormatKeyBinding(currentBinding);
                HasBinding = !string.IsNullOrEmpty(currentBinding.Key);
            }
        }

        private void OnKeyDown(object? sender, KeyEventArgs e) {
            if (_inputManager == null || !_isCaptureMode) return;

            // Handle escape to cancel
            if (e.Key == Key.Escape) {
                ExitCaptureMode();
                e.Handled = true;
                return;
            }

            // Check if this is a modifier key
            var keyName = e.Key.ToString();
            var isModifierKey = keyName.Contains("Control") || keyName.Contains("Ctrl") ||
                               keyName.Contains("Alt") || keyName.Contains("Shift") ||
                               keyName.Contains("Win") || keyName.Contains("Meta");

            if (isModifierKey) {
                // Don't handle yet - wait to see if another key is pressed or if this is released
                e.Handled = true;
                return;
            }

            // Get current modifier state as string - match Avalonia's KeyModifiers enum order: Alt, Control, Shift, Meta
            var modifiers = string.Empty;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) modifiers = "Alt";
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) modifiers = string.IsNullOrEmpty(modifiers) ? "Control" : modifiers + ", Control";
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers = string.IsNullOrEmpty(modifiers) ? "Shift" : modifiers + ", Shift";
            if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) modifiers = string.IsNullOrEmpty(modifiers) ? "Meta" : modifiers + ", Meta";

            // Set the new binding
            _inputManager.SetKeyBinding(Action, e.Key.ToString(), modifiers);
            
            ExitCaptureMode();
            
            // Update display properties
            CurrentKeyDisplay = FormatKeyBinding(new WorldBuilder.Shared.Models.KeyBinding(e.Key.ToString(), modifiers));
            HasBinding = !string.IsNullOrEmpty(e.Key.ToString());
            KeyDisplayTextBlock.Text = CurrentKeyDisplay;
            
            e.Handled = true;
        }

        private void OnKeyUp(object? sender, KeyEventArgs e) {
            if (_inputManager == null || !_isCaptureMode) return;

            // Check if this is a modifier key being released
            var keyName = e.Key.ToString();
            var isModifierKey = keyName.Contains("Control") || keyName.Contains("Ctrl") ||
                               keyName.Contains("Alt") || keyName.Contains("Shift") ||
                               keyName.Contains("Win") || keyName.Contains("Meta");

            if (isModifierKey) {
                // Check if any modifiers are currently held - match Avalonia's KeyModifiers enum order: Alt, Control, Shift, Meta
                var currentModifiers = string.Empty;
                if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) currentModifiers = "Alt";
                if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) currentModifiers = string.IsNullOrEmpty(currentModifiers) ? "Control" : currentModifiers + ", Control";
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) currentModifiers = string.IsNullOrEmpty(currentModifiers) ? "Shift" : currentModifiers + ", Shift";
                if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) currentModifiers = string.IsNullOrEmpty(currentModifiers) ? "Meta" : currentModifiers + ", Meta";

                // If no other modifiers are held, set this modifier as the main key
                if (string.IsNullOrEmpty(currentModifiers)) {
                    _inputManager.SetKeyBinding(Action, e.Key.ToString(), "");
                    
                    ExitCaptureMode();
                    
                    // Update display properties
                    CurrentKeyDisplay = FormatKeyBinding(new WorldBuilder.Shared.Models.KeyBinding(e.Key.ToString(), ""));
                    HasBinding = !string.IsNullOrEmpty(e.Key.ToString());
                    KeyDisplayTextBlock.Text = CurrentKeyDisplay;
                }
                
                e.Handled = true;
            }
        }

        private void OnGotFocus(object? sender, GotFocusEventArgs e) { }

        private void OnLostFocus(object? sender, RoutedEventArgs e) {
            // Exit capture mode if we lose focus
            if (_isCaptureMode) {
                ExitCaptureMode();
            }
        }
    }
}
