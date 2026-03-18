using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WorldBuilder.Controls;
using WorldBuilder.Modules.DatBrowser.ViewModels;

namespace WorldBuilder.Modules.DatBrowser.Views {
    public partial class DatBrowserView : UserControl {
        public DatBrowserView() {
            InitializeComponent();
        }

        private void OnKeyDown(object? sender, KeyEventArgs e) {
            if (DataContext is not DatBrowserViewModel viewModel) return;

            if (e.Key == Key.Enter) {
                var listBox = sender as ListBox;
                var selectedItem = listBox?.SelectedItem as TreeListNode<ReflectionNodeViewModel>;
                if (selectedItem != null) {
                    HandleExecuteItem(viewModel, selectedItem, e);
                    e.Handled = true;
                    
                    // Ensure the ListBoxItem retains focus after Enter key
                    Dispatcher.UIThread.Post(() => {
                        if (listBox != null) {
                            var listBoxItem = listBox.GetRealizedContainers()
                                .OfType<ListBoxItem>()
                                .FirstOrDefault(item => item.DataContext == selectedItem);
                            
                            if (listBoxItem != null) {
                                listBoxItem.Focus();
                            }
                        }
                    }, DispatcherPriority.Background);
                }
            }
        }

        private void OnDoubleTapped(object? sender, RoutedEventArgs e) {
            var listBox = sender as ListBox;
            var selectedItem = listBox?.SelectedItem as TreeListNode<ReflectionNodeViewModel>;
            if (selectedItem != null && DataContext is DatBrowserViewModel viewModel) {
                HandleExecuteItem(viewModel, selectedItem, e);
                
                // Ensure the ListBoxItem retains focus after double-click
                Dispatcher.UIThread.Post(() => {
                    if (listBox != null) {
                        var listBoxItem = listBox.GetRealizedContainers()
                            .OfType<ListBoxItem>()
                            .FirstOrDefault(item => item.DataContext == selectedItem);
                        
                        if (listBoxItem != null) {
                            listBoxItem.Focus();
                        }
                    }
                }, DispatcherPriority.Background);
            }
        }

        private void HandleExecuteItem(DatBrowserViewModel viewModel, TreeListNode<ReflectionNodeViewModel>? selectedItem, RoutedEventArgs e) {
            if (selectedItem != null && selectedItem.HasChildren) {
                viewModel.ToggleExpandedCommand.Execute(selectedItem);
                e.Handled = true;
            }
        }
    }
}
