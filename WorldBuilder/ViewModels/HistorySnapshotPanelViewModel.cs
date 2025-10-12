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
using WorldBuilder.Lib.History;
using WorldBuilder.Shared.Documents;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.ViewModels {
    public partial class HistorySnapshotPanelViewModel : ViewModelBase {
        private readonly TerrainSystem _terrainSystem;
        private readonly IDocumentStorageService _documentStorageService;
        private readonly CommandHistory _commandHistory;
        private bool _isUpdatingSelection;
        private List<DBSnapshot> _cachedSnapshots;
        private byte[]? _originalDocumentProjection;
        private byte[]? _baseProjection;

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
            // Capture the original document state (never changes)
            _originalDocumentProjection = _terrainSystem.TerrainDoc.SaveToProjection();
            _baseProjection = _originalDocumentProjection;
            UpdateHistoryList();
        }

        private void OnCommandHistoryChanged(object? sender, EventArgs e) {
            UpdateHistoryList();
        }

        private async Task LoadSnapshotsAsync() {
            _cachedSnapshots = (await _documentStorageService.GetSnapshotsAsync(_terrainSystem.TerrainDoc.Id)).ToList();
            UpdateSnapshotItems();
        }

        private void UpdateSnapshotItems() {
            var selectedSnapshotId = SelectedItem?.IsSnapshot == true ? SelectedItem.SnapshotId : null;

            SnapshotItems.Clear();
            var snapshotItems = _cachedSnapshots
                .Select(s => new HistoryListItem {
                    Index = -1,
                    Description = s.Name,
                    Timestamp = s.Timestamp,
                    IsCurrent = false,
                    IsSnapshot = true,
                    SnapshotId = s.Id
                })
                .OrderBy(i => i.Timestamp);

            foreach (var item in snapshotItems) {
                SnapshotItems.Add(item);
            }

            // Restore snapshot selection if it still exists
            if (selectedSnapshotId.HasValue) {
                var snapshotToSelect = SnapshotItems.FirstOrDefault(i => i.SnapshotId == selectedSnapshotId);
                if (snapshotToSelect != null) {
                    _isUpdatingSelection = true;
                    SelectedSnapshot = snapshotToSelect;
                    SelectedItem = snapshotToSelect;
                    SelectedHistory = null;
                    _isUpdatingSelection = false;
                }
            }
        }

        private void UpdateHistoryList() {
            var currentIsSnapshot = SelectedItem?.IsSnapshot == true;

            var newHistoryItems = _commandHistory.GetHistoryList().OrderBy(i => i.Index).ToList();
            UpdateCollection(HistoryItems, newHistoryItems);

            // If a snapshot is currently selected AND there's no new history, keep the snapshot selected
            if (currentIsSnapshot && _commandHistory.CurrentIndex == -1) {
                UpdateDimming();
                return;
            }

            _isUpdatingSelection = true;
            try {
                // Auto-select the current history item 
                var itemToSelect = HistoryItems.FirstOrDefault(i => i.IsCurrent)
                    ?? HistoryItems.FirstOrDefault(i => i.Index == -1);

                SelectedHistory = itemToSelect;
                SelectedItem = itemToSelect;
                SelectedSnapshot = null; // Clear snapshot when new history is created
            }
            finally {
                _isUpdatingSelection = false;
            }

            UpdateCanRevert();
            UpdateDimming();
        }

        private void UpdateCollection(ObservableCollection<HistoryListItem> currentItems, List<HistoryListItem> newItems) {
            // Remove items that no longer exist
            for (int i = currentItems.Count - 1; i >= 0; i--) {
                var current = currentItems[i];
                if (!newItems.Any(n => n.Index == current.Index)) {
                    currentItems.RemoveAt(i);
                }
            }

            // Update or insert items
            for (int i = 0; i < newItems.Count; i++) {
                var newItem = newItems[i];
                if (i < currentItems.Count && currentItems[i].Index == newItem.Index) {
                    // Update existing item
                    currentItems[i].Description = newItem.Description;
                    currentItems[i].Timestamp = newItem.Timestamp;
                    currentItems[i].IsCurrent = newItem.IsCurrent;
                }
                else {
                    // Insert new item
                    currentItems.Insert(i, newItem);
                }
            }

            // Remove excess items
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
            // Otherwise, only dim future history items (beyond current index)
            foreach (var item in HistoryItems) {
                if (isSnapshotSelected) {
                    item.IsDimmed = true;
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
            UpdateSnapshotItems();
        }

        [RelayCommand]
        private async Task RevertToState(HistoryListItem? item) {
            if (item == null) return;

            var startLandblocks = _terrainSystem.TerrainDoc.TerrainData.Landblocks.Keys.ToHashSet();
            bool success = false;

            if (item.IsSnapshot) {
                // Load snapshot directly
                var snapshot = _cachedSnapshots.FirstOrDefault(s => s.Id == item.SnapshotId!.Value)
                    ?? await _documentStorageService.GetSnapshotAsync(item.SnapshotId!.Value);

                if (snapshot != null) {
                    success = _terrainSystem.TerrainDoc.LoadFromProjection(snapshot.Data);
                    if (success) {
                        // Update the working base projection to the snapshot state
                        // This ensures new edits build on top of the snapshot
                        _baseProjection = snapshot.Data;

                        // Reset history to base state
                        _commandHistory.HistoryChanged -= OnCommandHistoryChanged;
                        _commandHistory.ResetToBase();
                        _commandHistory.HistoryChanged += OnCommandHistoryChanged;
                    }
                }
            }
            else {
                // Determine which base to use
                byte[]? baseToUse;
                if (item.Index == -1) {
                    // "Original Document" entry - always use the original
                    baseToUse = _originalDocumentProjection;
                    _baseProjection = _originalDocumentProjection; // Reset working base to original
                }
                else {
                    // History entry - use current working base (could be original or snapshot)
                    baseToUse = _baseProjection;
                }

                if (baseToUse == null) {
                    baseToUse = _terrainSystem.TerrainDoc.SaveToProjection();
                    _originalDocumentProjection ??= baseToUse;
                    _baseProjection = baseToUse;
                }

                success = _terrainSystem.TerrainDoc.LoadFromProjection(baseToUse);

                if (success) {
                    // Replay history to the target index
                    if (item.Index >= 0) {
                        _commandHistory.HistoryChanged -= OnCommandHistoryChanged;
                        _commandHistory.ResetToBase();
                        success = _commandHistory.JumpToHistory(item.Index);
                        _commandHistory.HistoryChanged += OnCommandHistoryChanged;
                    }
                    else {
                        // Just stay at base state (index -1)
                        _commandHistory.HistoryChanged -= OnCommandHistoryChanged;
                        _commandHistory.ResetToBase();
                        _commandHistory.HistoryChanged += OnCommandHistoryChanged;
                    }
                }
            }

            if (success) {
                // Mark all affected landblocks as modified
                var modifiedLandblockIds = new HashSet<ushort>(_terrainSystem.TerrainDoc.TerrainData.Landblocks.Keys);
                modifiedLandblockIds.UnionWith(startLandblocks);
                _terrainSystem.EditingContext.MarkLandblocksModified(modifiedLandblockIds);

                // Force a complete refresh
                UpdateHistoryList();
            }
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

                // Clear selection if we deleted the selected snapshot
                if (SelectedItem?.SnapshotId == item.SnapshotId) {
                    _isUpdatingSelection = true;
                    SelectedSnapshot = null;
                    SelectedItem = null;
                    _isUpdatingSelection = false;

                    // Revert to current history state
                    var currentHistoryItem = HistoryItems.FirstOrDefault(i => i.IsCurrent);
                    await RevertToState(currentHistoryItem);
                }

                UpdateSnapshotItems();
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
                UpdateHistoryList();
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

                var cachedSnapshot = _cachedSnapshots.FirstOrDefault(s => s.Id == snapshot.Id);
                if (cachedSnapshot != null) {
                    cachedSnapshot.Name = newName;
                }

                UpdateSnapshotItems();
            }
        }

        [RelayCommand]
        private void SelectSnapshot(HistoryListItem? item) {
            if (item == null || _isUpdatingSelection) return;

            _isUpdatingSelection = true;
            try {
                SelectedSnapshot = item;
                SelectedItem = item;
                SelectedHistory = null;

                UpdateDimming();
                UpdateCanRevert();

                _ = RevertToState(item);
            }
            finally {
                _isUpdatingSelection = false;
            }
        }

        [RelayCommand]
        private void SelectHistoryItem(HistoryListItem? item) {
            if (item == null || _isUpdatingSelection) return;

            _isUpdatingSelection = true;
            try {
                SelectedHistory = item;
                SelectedItem = item;
                SelectedSnapshot = null;

                UpdateDimming();
                UpdateCanRevert();

                _ = RevertToState(item);
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