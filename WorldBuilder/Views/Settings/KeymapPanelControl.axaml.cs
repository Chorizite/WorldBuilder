using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Controls;
using WorldBuilder.Lib.Input;

namespace WorldBuilder.Views.Settings {
    public partial class KeymapPanelControl : UserControl {
        private InputManager? _inputManager;
        private Dictionary<string, List<InputAction>>? _categorizedActions;
        private List<KeymapEntryControl>? _entryControls;

        public KeymapPanelControl() {
            InitializeComponent();
            
            // Get InputManager from service provider
            _inputManager = WorldBuilder.App.Services?.GetService<InputManager>()
                ?? throw new InvalidOperationException("InputManager not available");

            _entryControls = new List<KeymapEntryControl>();
            _categorizedActions = new Dictionary<string, List<InputAction>>();
                
            // Set DataContext to self for XAML bindings
            DataContext = this;
                
            // Subscribe to key bindings changes
            _inputManager.KeyBindingsChanged += OnKeyBindingsChanged;
                
            LoadCategorizedActions();
            PopulateKeyBindings();
        }

        private void LoadCategorizedActions() {
            if (_categorizedActions == null) return;
            
            _categorizedActions.Clear();

            // Collect category order information
            var categoryOrders = new Dictionary<string, int>();

            foreach (InputAction action in Enum.GetValues<InputAction>()) {
                if (action == InputAction.None) continue;

                var category = GetActionCategory(action);
                var categoryOrder = GetActionCategoryOrder(action);
                
                if (!_categorizedActions.ContainsKey(category)) {
                    _categorizedActions[category] = new List<InputAction>();
                    categoryOrders[category] = categoryOrder;
                }
                
                _categorizedActions[category].Add(action);
            }

            // Sort categories by order, then by name
            var sortedCategories = _categorizedActions.Keys
                .OrderBy(k => categoryOrders.GetValueOrDefault(k, 0))
                .ThenBy(k => k)
                .ToList();

            // Sort actions within each category by their order
            foreach (var category in sortedCategories) {
                _categorizedActions[category] = _categorizedActions[category]
                    .OrderBy(a => GetActionOrder(a))
                    .ToList();
            }
        }

        private string GetActionCategory(InputAction action) {
            var field = typeof(InputAction).GetField(action.ToString());
            var attr = field?.GetCustomAttribute<CategoryAttribute>();
            
            return attr?.Name ?? "Other";
        }

        private int GetActionCategoryOrder(InputAction action) {
            var field = typeof(InputAction).GetField(action.ToString());
            var attr = field?.GetCustomAttribute<CategoryAttribute>();
            
            return attr?.Order ?? 0;
        }

        private int GetActionOrder(InputAction action) {
            var field = typeof(InputAction).GetField(action.ToString());
            var attr = field?.GetCustomAttribute<DefaultKeyAttribute>();
            
            return attr?.Order ?? 0;
        }

        private void PopulateKeyBindings() {
            if (_categorizedActions == null || _entryControls == null) return;
            
            KeyBindingsStackPanel.Children.Clear();
            _entryControls.Clear();

            // Get categories in the order they were sorted in LoadCategorizedActions
            foreach (var category in _categorizedActions.Keys) {
                // Add category header
                var categoryHeader = new TextBlock {
                    Text = category
                };
                categoryHeader.Classes.Add("category-header");
                KeyBindingsStackPanel.Children.Add(categoryHeader);

                // Add key entries for this category (already sorted by order)
                foreach (var action in _categorizedActions[category]) {
                    var entryControl = new KeymapEntryControl {
                        Action = action,
                        ActionName = FormatActionName(action)
                    };
                    
                    KeyBindingsStackPanel.Children.Add(entryControl);
                    _entryControls.Add(entryControl);
                }
            }
        }

        private string FormatActionName(InputAction action) {
            // Convert PascalCase to readable format
            return System.Text.RegularExpressions.Regex.Replace(action.ToString(), "([a-z])([A-Z])", "$1 $2");
        }

        private void OnSearchTextChanged(object? sender, TextChangedEventArgs e) {
            if (_entryControls == null) return;
            
            var searchText = SearchTextBox.Text?.ToLowerInvariant() ?? string.Empty;
            
            if (string.IsNullOrEmpty(searchText)) {
                ShowAllEntries();
                return;
            }

            // Filter entries based on search
            var visibleCategories = new HashSet<string>();
            
            foreach (var entryControl in _entryControls) {
                var isVisible = entryControl.ActionName.ToLowerInvariant().Contains(searchText) ||
                               entryControl.CurrentKeyDisplay.ToLowerInvariant().Contains(searchText);
                
                entryControl.IsVisible = isVisible;
                
                if (isVisible) {
                    var category = GetActionCategory(entryControl.Action);
                    visibleCategories.Add(category);
                }
            }

            // Show/hide category headers based on visible entries
            HideAllCategoryHeaders();
            ShowCategoryHeaders(visibleCategories);
        }

        private void ShowAllEntries() {
            if (_entryControls == null) return;
            
            foreach (var entryControl in _entryControls) {
                entryControl.IsVisible = true;
            }
            
            ShowAllCategoryHeaders();
        }

        private void HideAllCategoryHeaders() {
            for (int i = 0; i < KeyBindingsStackPanel.Children.Count; i++) {
                if (KeyBindingsStackPanel.Children[i] is TextBlock header) {
                    header.IsVisible = false;
                }
            }
        }

        private void ShowAllCategoryHeaders() {
            for (int i = 0; i < KeyBindingsStackPanel.Children.Count; i++) {
                if (KeyBindingsStackPanel.Children[i] is TextBlock header) {
                    header.IsVisible = true;
                }
            }
        }

        private void ShowCategoryHeaders(HashSet<string> visibleCategories) {
            for (int i = 0; i < KeyBindingsStackPanel.Children.Count; i++) {
                if (KeyBindingsStackPanel.Children[i] is TextBlock header && header.Text != null) {
                    header.IsVisible = visibleCategories.Contains(header.Text);
                }
            }
        }

        private void OnKeyBindingsChanged(object? sender, EventArgs e) {
            // Refresh all keymap entry displays when bindings are reloaded
            if (_entryControls != null) {
                foreach (var entryControl in _entryControls) {
                    entryControl.UpdateDisplay();
                }
            }
        }
    }
}
