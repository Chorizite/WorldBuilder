using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogHostAvalonia;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using WorldBuilder.Editors.Landscape;
using WorldBuilder.Lib;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.ViewModels {
    public partial class HistorySnapshotPanelViewModel : ViewModelBase {
        private readonly TerrainSystem _terrainSystem;
        private readonly IDocumentStorageService _documentStorageService;
        private readonly CommandHistory _commandHistory;
        private bool _isUpdatingSelection;
        private List<DBSnapshot> _cachedSnapshots;
        private byte[]? _tempOriginalProjection;

        [ObservableProperty]
        private ObservableCollection<HistoryListItem> _snapshotItems;

        [ObservableProperty]
        private ObservableCollection<HistoryListItem> _historyItems;

        [ObservableProperty]
        private HistoryListItem? _selectedSnapshot;

        [ObservableProperty]
        private HistoryListItem? _selectedHistory;

        [ObservableProperty]
        private HistoryListItem? _selectedItem;

        [ObservableProperty]
        private bool _canRevert;

        public HistorySnapshotPanelViewModel(
            TerrainSystem terrainSystem,
            IDocumentStorageService documentStorageService,
            CommandHistory commandHistory) {
            _terrainSystem = terrainSystem ?? throw new ArgumentNullException(nameof(terrainSystem));
            _documentStorageService = documentStorageService ?? throw new ArgumentNullException(nameof(documentStorageService));
            _commandHistory = commandHistory ?? throw new ArgumentNullException(nameof(commandHistory));
            _snapshotItems = new ObservableCollection<HistoryListItem>();
            _historyItems = new ObservableCollection<HistoryListItem>();
            _cachedSnapshots = new List<DBSnapshot>();

            _commandHistory.HistoryChanged += OnCommandHistoryChanged;
            InitializeAsync();
        }

        private async void InitializeAsync() {
            await LoadSnapshotsAsync();
            _tempOriginalProjection = _terrainSystem.TerrainDoc.SaveToProjection();
            UpdateHistoryList(null);
        }

        private void OnCommandHistoryChanged(object? sender, EventArgs e) {
            UpdateHistoryList(SelectedItem);
            UpdateTempOriginal();
        }

        private void UpdateTempOriginal() {
            if (_commandHistory.History.Count == 0) {
                // If history is empty, save the current document state as the temporary original
                _tempOriginalProjection = _terrainSystem.TerrainDoc.SaveToProjection();
            }
            else {
                // Temporarily disable history change events to prevent recursive updates
                _commandHistory.HistoryChanged -= OnCommandHistoryChanged;
                try {
                    int currentIndex = _commandHistory.CurrentIndex;
                    // Jump to the base state (Index = -1) to capture the original state
                    if (_commandHistory.JumpToHistory(-1)) {
                        _tempOriginalProjection = _terrainSystem.TerrainDoc.SaveToProjection();
                        // Restore to the original history index
                        _commandHistory.JumpToHistory(currentIndex);
                    }
                }
                finally {
                    _commandHistory.HistoryChanged += OnCommandHistoryChanged;
                }
            }
        }

        private async Task LoadSnapshotsAsync() {
            _cachedSnapshots = (await _documentStorageService.GetSnapshotsAsync(_terrainSystem.TerrainDoc.Id)).ToList();
            UpdateSnapshotItems(null);
        }

        private void UpdateSnapshotItems(HistoryListItem? targetSelection) {
            var selectedSnapshotId = targetSelection?.IsSnapshot == true ? targetSelection.SnapshotId : SelectedItem?.IsSnapshot == true ? SelectedItem.SnapshotId : null;
            SnapshotItems.Clear();
            var snapshotItems = _cachedSnapshots.Select(s => new HistoryListItem {
                Index = -1,
                Description = s.Name,
                Timestamp = s.Timestamp,
                IsCurrent = false,
                IsSnapshot = true,
                SnapshotId = s.Id
            }).OrderBy(i => i.Timestamp);

            foreach (var item in snapshotItems) {
                SnapshotItems.Add(item);
            }

            if (selectedSnapshotId.HasValue) {
                _isUpdatingSelection = true;
                var snapshotToSelect = SnapshotItems.FirstOrDefault(i => i.SnapshotId == selectedSnapshotId);
                SelectedSnapshot = snapshotToSelect;
                SelectedItem = snapshotToSelect;
                SelectedHistory = null;
                _isUpdatingSelection = false;
            }
        }

        private void UpdateHistoryList(HistoryListItem? targetSelection, bool isUserSelection = false) {
            var selectedIndex = targetSelection?.IsSnapshot == false ? targetSelection.Index : -1;
            var wasSnapshot = targetSelection?.IsSnapshot == true;

            var newHistoryItems = _commandHistory.GetHistoryList().OrderBy(i => i.Index).ToList();
            UpdateCollection(HistoryItems, newHistoryItems);

            UpdateDimming();

            // If a snapshot is selected, don't update history selection
            if (wasSnapshot) {
                return;
            }

            _isUpdatingSelection = true;
            try {
                // Default to the current item
                var itemToSelect = HistoryItems.FirstOrDefault(i => i.IsCurrent) ?? HistoryItems.LastOrDefault();

                // Only use targetSelection for explicit user selections (e.g., UI click)
                if (isUserSelection && targetSelection != null && !wasSnapshot && selectedIndex >= -1) {
                    var matchingItem = HistoryItems.FirstOrDefault(i => i.Index == selectedIndex);
                    if (matchingItem != null) {
                        itemToSelect = matchingItem;
                    }
                }

                SelectedHistory = itemToSelect;
                SelectedItem = itemToSelect;
                SelectedSnapshot = null; // Clear snapshot selection when selecting history
            }
            finally {
                _isUpdatingSelection = false;
            }
            UpdateCanRevert();
        }

        private void UpdateCollection(ObservableCollection<HistoryListItem> currentItems, List<HistoryListItem> newItems) {
            for (int i = currentItems.Count - 1; i >= 0; i--) {
                var current = currentItems[i];
                if (!newItems.Any(n => n.Index == current.Index && n.IsSnapshot == current.IsSnapshot)) {
                    currentItems.RemoveAt(i);
                }
            }

            for (int i = 0; i < newItems.Count; i++) {
                var newItem = newItems[i];
                if (i < currentItems.Count && currentItems[i].Index == newItem.Index) {
                    currentItems[i].Description = newItem.Description;
                    currentItems[i].Timestamp = newItem.Timestamp;
                    currentItems[i].IsCurrent = newItem.IsCurrent;
                }
                else {
                    currentItems.Insert(i, newItem);
                }
            }
            while (currentItems.Count > newItems.Count) {
                currentItems.RemoveAt(currentItems.Count - 1);
            }
        }

        private void UpdateCanRevert() {
            CanRevert = SelectedItem != null && !SelectedItem.IsCurrent;
        }

        private void UpdateDimming() {
            var currentIndex = _commandHistory.CurrentIndex;
            var isSnapshotSelected = SelectedItem?.IsSnapshot ?? false;

            // Snapshots are never dimmed
            foreach (var item in SnapshotItems) {
                item.IsDimmed = false;
            }

            // When a snapshot is selected, dim ALL history items
            // Otherwise, only dim forward history items
            foreach (var item in HistoryItems) {
                if (isSnapshotSelected) {
                    item.IsDimmed = true;
                }
                else if (item.IsOriginalDocument) {
                    item.IsDimmed = false;
                }
                else {
                    item.IsDimmed = item.Index > currentIndex;
                }
            }
        }

        [RelayCommand]
        private async Task CreateSnapshot() {
            var snapshotCount = SnapshotItems.Count + 1;
            var snapshotName = $"Snapshot {snapshotCount}";

            var projection = _terrainSystem.TerrainDoc.SaveToProjection();
            var snapshot = new DBSnapshot {
                Id = Guid.NewGuid(),
                DocumentId = _terrainSystem.TerrainDoc.Id,
                Name = snapshotName,
                Data = projection,
                Timestamp = DateTime.UtcNow
            };

            await _documentStorageService.CreateSnapshotAsync(snapshot);
            _cachedSnapshots.Add(snapshot);
            UpdateSnapshotItems(SelectedItem);
        }

        [RelayCommand]
        private async Task RevertToState(HistoryListItem? item) {
            if (item == null) return;

            var startLandblocks = _terrainSystem.TerrainDoc.TerrainData.Landblocks.Keys.ToHashSet();
            bool success = false;

            if (item.IsSnapshot) {
                var snapshot = _cachedSnapshots.FirstOrDefault(s => s.Id == item.SnapshotId!.Value)
                    ?? await _documentStorageService.GetSnapshotAsync(item.SnapshotId!.Value);
                if (snapshot != null) {
                    success = _terrainSystem.TerrainDoc.LoadFromProjection(snapshot.Data);
                    // Reset history to base state when loading a snapshot
                    _commandHistory.ResetToBase();
                }
            }
            else {
                if (_tempOriginalProjection == null) {
                    // Fallback: Save current state as temp original if not set
                    _tempOriginalProjection = _terrainSystem.TerrainDoc.SaveToProjection();
                }

                // Always start from the original state for history navigation
                success = _terrainSystem.TerrainDoc.LoadFromProjection(_tempOriginalProjection);
                Console.WriteLine($"Reverting to original state: {success}");
                if (success && item.Index >= 0) {
                    // Only reset to base and jump if not selecting the original document
                    _commandHistory.ResetToBase();
                    success = _commandHistory.JumpToHistory(item.Index);
                }
                else if (success && item.Index == -1) {
                    // For original document, just reset to base state
                    _commandHistory.ResetToBase();
                }
            }

            if (success) {
                var modifiedLandblockIds = new HashSet<ushort>(_terrainSystem.TerrainDoc.TerrainData.Landblocks.Keys);
                modifiedLandblockIds.UnionWith(startLandblocks);
                _terrainSystem.EditingContext.MarkLandblocksModified(modifiedLandblockIds);
            }

            UpdateHistoryList(item, isUserSelection: true);
        }

        [RelayCommand]
        private async Task DeleteItem(HistoryListItem? item) {
            if (item == null) return;

            // Prevent deletion of "Original Document" entry
            if (!item.IsSnapshot && item.IsOriginalDocument) {
                return;
            }

            if (item.IsSnapshot) {
                await DeleteSnapshot(item);
            }
            else {
                await DeleteHistoryEntry(item);
            }
        }

        private async Task DeleteSnapshot(HistoryListItem item) {
            var confirmed = await ShowConfirmationDialog(
                "Delete Snapshot",
                $"Are you sure you want to delete '{item.Description}'?");

            if (confirmed) {
                await _documentStorageService.DeleteSnapshotAsync(item.SnapshotId!.Value);
                _cachedSnapshots.RemoveAll(s => s.Id == item.SnapshotId!.Value);
                UpdateSnapshotItems(SelectedItem?.SnapshotId == item.SnapshotId ? null : SelectedItem);
            }
        }

        private async Task DeleteHistoryEntry(HistoryListItem item) {
            var forwardCount = _commandHistory.History.Count - item.Index - 1;
            var message = forwardCount > 0 && item.Index <= _commandHistory.CurrentIndex
                ? $"Deleting '{item.Description}' will also delete {forwardCount} forward history entries. Continue?"
                : $"Are you sure you want to delete '{item.Description}'?";

            var confirmed = await ShowConfirmationDialog("Delete History Entry", message);

            if (confirmed) {
                _commandHistory.DeleteFromIndex(item.Index);
                UpdateHistoryList(SelectedItem?.Index == item.Index ? null : SelectedItem);
            }
        }

        [RelayCommand]
        private async Task RenameSnapshot(HistoryListItem? item) {
            if (item == null || !item.IsSnapshot) return;

            var snapshot = _cachedSnapshots.FirstOrDefault(s => s.Id == item.SnapshotId!.Value);
            if (snapshot == null) return;

            var newName = await ShowRenameDialog("Rename Snapshot", snapshot.Name);
            if (!string.IsNullOrWhiteSpace(newName) && newName != snapshot.Name) {
                snapshot.Name = newName;
                await _documentStorageService.UpdateSnapshotNameAsync(snapshot.Id, newName);

                // Update the cached snapshot and UI
                var cachedSnapshot = _cachedSnapshots.FirstOrDefault(s => s.Id == snapshot.Id);
                if (cachedSnapshot != null) {
                    cachedSnapshot.Name = newName;
                }

                UpdateSnapshotItems(SelectedItem);
            }
        }

        [RelayCommand]
        private void SelectSnapshot(HistoryListItem? item) {
            if (item == null || _isUpdatingSelection) return;

            _isUpdatingSelection = true;
            try {
                SelectedSnapshot = item;
                SelectedItem = item;

                UpdateDimming();
                UpdateCanRevert();

                _ = RevertToState(item);

                SelectedHistory = null;
            }
            finally {
                _isUpdatingSelection = false;
            }
        }

        [RelayCommand]
        private void SelectHistoryItem(HistoryListItem? item) {
            Console.WriteLine($"SelectHistoryItem: {item?.Description} // {item?.IsCurrent}");
            if (item == null || _isUpdatingSelection) return;

            _isUpdatingSelection = true;
            try {
                SelectedHistory = item;
                SelectedItem = item;

                UpdateDimming();
                UpdateCanRevert();

                _ = RevertToState(item);

                SelectedSnapshot = null;
            }
            finally {
                _isUpdatingSelection = false;
            }
        }

        private async Task<bool> ShowConfirmationDialog(string title, string message) {
            bool result = false;

            await DialogHost.Show(new Avalonia.Controls.StackPanel {
                Margin = new Avalonia.Thickness(20),
                Spacing = 15,
                Children = {
                    new Avalonia.Controls.TextBlock {
                        Text = title,
                        FontSize = 16,
                        FontWeight = Avalonia.Media.FontWeight.Bold
                    },
                    new Avalonia.Controls.TextBlock {
                        Text = message,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                        MaxWidth = 400
                    },
                    new Avalonia.Controls.StackPanel {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = {
                            new Avalonia.Controls.Button {
                                Content = "Cancel",
                                Command = new RelayCommand(() => DialogHost.Close("MainDialogHost"))
                            },
                            new Avalonia.Controls.Button {
                                Content = "Delete",
                                Command = new RelayCommand(() => {
                                    result = true;
                                    DialogHost.Close("MainDialogHost");
                                })
                            }
                        }
                    }
                }
            }, "MainDialogHost");

            return result;
        }

        private async Task<string?> ShowRenameDialog(string title, string currentName) {
            string? result = null;
            var textBox = new Avalonia.Controls.TextBox {
                Text = currentName,
                Width = 300,
                Watermark = "Enter snapshot name"
            };

            await DialogHost.Show(new Avalonia.Controls.StackPanel {
                Margin = new Avalonia.Thickness(20),
                Spacing = 15,
                Children = {
                    new Avalonia.Controls.TextBlock {
                        Text = title,
                        FontSize = 16,
                        FontWeight = Avalonia.Media.FontWeight.Bold
                    },
                    textBox,
                    new Avalonia.Controls.StackPanel {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = {
                            new Avalonia.Controls.Button {
                                Content = "Cancel",
                                Command = new RelayCommand(() => DialogHost.Close("MainDialogHost"))
                            },
                            new Avalonia.Controls.Button {
                                Content = "Rename",
                                Command = new RelayCommand(() => {
                                    result = textBox.Text;
                                    DialogHost.Close("MainDialogHost");
                                })
                            }
                        }
                    }
                }
            }, "MainDialogHost");

            return result;
        }

        public void Dispose() {
            _commandHistory.HistoryChanged -= OnCommandHistoryChanged;
        }
    }
}