using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using WorldBuilder.Editors;
using WorldBuilder.Editors.Landscape;
using WorldBuilder.Lib;
using WorldBuilder.Lib.History;
using WorldBuilder.Lib.Settings;
using WorldBuilder.Shared.Documents;
using WorldBuilder.ViewModels;

public class CommandHistory {
    private readonly List<HistoryEntry> _history = new();
    private int _currentIndex = -1;
    private readonly ILogger _logger;
    private readonly AppSettings _settings;
    private readonly IEditor _editor;

    private int _maxHistorySize => _settings.HistoryLimit;

    public event EventHandler? HistoryChanged;

    public bool CanUndo => _currentIndex >= 0 && _history.Count > 0;
    public bool CanRedo => _currentIndex < _history.Count - 1 && _history.Count > 0;
    public int CurrentIndex => _currentIndex;
    public IReadOnlyList<HistoryEntry> History => _history.AsReadOnly();

    public CommandHistory(AppSettings settings, IEditor editor, ILogger logger) {
        _logger = logger;
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        ValidateIndex();

        _settings.PropertyChanged += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e) {
        if (e.PropertyName == nameof(AppSettings.HistoryLimit)) {
            TrimHistory();
        }
    }

    public bool ExecuteCommand(ICommand command) => ExecuteCommandAsync(command).GetAwaiter().GetResult();
    public bool Undo() => UndoAsync().GetAwaiter().GetResult();
    public bool Redo() => RedoAsync().GetAwaiter().GetResult();
    public bool JumpToHistory(int targetIndex) => JumpToHistoryAsync(targetIndex).GetAwaiter().GetResult();
    public bool DeleteFromIndex(int index) => DeleteFromIndexAsync(index).GetAwaiter().GetResult();

    public async Task<bool> ExecuteCommandAsync(ICommand command) {
        if (command == null) return false;
        if (!command.Execute()) return false;

        var entry = new HistoryEntry(command) {
            AffectedDocumentIds = command.AffectedDocumentIds ?? new List<string>()
        };

        foreach (var docId in entry.AffectedDocumentIds) {
            var doc = _editor.GetDocument(docId);
            if (doc != null) {
                doc.MarkDirty();
            }
            else {
                var docType = GetDocumentTypeFromId(docId);
                var loadedDoc = await _editor.LoadDocumentAsync(docId, docType);
                if (loadedDoc != null) {
                    loadedDoc.MarkDirty();
                }
                else {
                    _logger.LogWarning("Failed to load document {DocumentId} of type {Type}", docId, docType.Name);
                }
            }
        }

        if (_currentIndex < _history.Count - 1) {
            _history.RemoveRange(_currentIndex + 1, _history.Count - _currentIndex - 1);
        }
        _history.Add(entry);
        _currentIndex++;
        TrimHistory();
        UpdateCurrentStateMarkers();
        OnHistoryChanged();
        return true;
    }

    public async Task<bool> UndoAsync() {
        if (!CanUndo) return false;
        var entry = _history[_currentIndex];
        var loadedTemp = await LoadTempDocsAsync(entry.AffectedDocumentIds);
        try {
            if (entry.Command.Undo()) {
                foreach (var docId in entry.AffectedDocumentIds) {
                    if (_editor.GetDocument(docId) is BaseDocument doc) {
                        doc.MarkDirty();
                    }
                }
                _currentIndex--;
                UpdateCurrentStateMarkers();
                OnHistoryChanged();
                return true;
            }
        }
        finally {
            await UnloadTempDocsAsync(loadedTemp);
        }
        return false;
    }

    public async Task<bool> RedoAsync() {
        if (!CanRedo) return false;
        var entry = _history[_currentIndex + 1];
        var loadedTemp = await LoadTempDocsAsync(entry.AffectedDocumentIds);
        try {
            if (entry.Command.Execute()) {
                foreach (var docId in entry.AffectedDocumentIds) {
                    if (_editor.GetDocument(docId) is BaseDocument doc) {
                        doc.MarkDirty();
                    }
                }
                _currentIndex++;
                UpdateCurrentStateMarkers();
                OnHistoryChanged();
                return true;
            }
        }
        finally {
            await UnloadTempDocsAsync(loadedTemp);
        }
        return false;
    }

    private Type GetDocumentTypeFromId(string documentId) {
        if (documentId == "terrain") {
            return typeof(TerrainDocument);
        }
        if (documentId.StartsWith("landblock_", StringComparison.OrdinalIgnoreCase)) {
            return typeof(LandblockDocument);
        }
        throw new ArgumentException($"Unknown document type for ID {documentId}", nameof(documentId));
    }

    private async Task<List<string>> LoadTempDocsAsync(List<string> docIds) {
        var tempLoaded = new List<string>();
        foreach (var id in docIds) {
            if (_editor.GetDocument(id) == null) {
                var docType = GetDocumentTypeFromId(id);
                var doc = await _editor.LoadDocumentAsync(id, docType);
                if (doc != null) {
                    tempLoaded.Add(id);
                }
                else {
                    _logger.LogWarning("Failed to load document {DocumentId} of type {Type}", id, docType.Name);
                }
            }
        }
        return tempLoaded;
    }

    private async Task UnloadTempDocsAsync(List<string> tempLoaded) {
        foreach (var id in tempLoaded) {
            if (_editor.GetDocument(id) is BaseDocument doc && !IsDocumentInView(id)) {
                await _editor.UnloadDocumentAsync(id);
            }
        }
    }

    private bool IsDocumentInView(string docId) {
        if (docId == "terrain") return true; // Terrain is always loaded
        if (!docId.StartsWith("landblock_", StringComparison.OrdinalIgnoreCase)) return false;

        // Extract landblock coordinates from ID (e.g., "landblock_0x1234" -> x=0x12, y=0x34)
        var lbKey = ushort.Parse(docId.Replace("landblock_", ""), System.Globalization.NumberStyles.HexNumber);
        var lbX = (lbKey >> 8) & 0xFF;
        var lbY = lbKey & 0xFF;

        // Get camera position from TerrainSystem (assumes IEditor is TerrainSystem)
        if (_editor is TerrainSystem terrainSystem && terrainSystem.Scene.CameraManager?.Current is ICamera camera) {
            var cameraPos = camera.Position;
            var lbCenter = new Vector2(lbX * TerrainDataManager.LandblockLength + TerrainDataManager.LandblockLength / 2,
                                       lbY * TerrainDataManager.LandblockLength + TerrainDataManager.LandblockLength / 2);
            var dist2D = Vector2.Distance(new Vector2(cameraPos.X, cameraPos.Y), lbCenter);
            return dist2D <= 500f; // ProximityThreshold from TerrainSystem
        }
        return false; // Default to unload if camera unavailable
    }

    public async Task<bool> JumpToHistoryAsync(int targetIndex) {
        if (targetIndex < -1 || targetIndex >= _history.Count || _history.Count == 0) return false;
        if (targetIndex == _currentIndex) return true;

        try {
            while (_currentIndex < targetIndex) {
                if (!await RedoAsync()) return false;
            }
            while (_currentIndex > targetIndex) {
                if (!await UndoAsync()) return false;
            }
            ValidateIndex();
            return true;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to jump to history index {TargetIndex}", targetIndex);
            _currentIndex = Math.Max(-1, _history.Count - 1);
            UpdateCurrentStateMarkers();
            OnHistoryChanged();
            return false;
        }
    }

    public async Task<bool> DeleteFromIndexAsync(int index) {
        if (index < 0 || index >= _history.Count || _history.Count == 0) return false;

        try {
            if (index <= _currentIndex) {
                if (!await JumpToHistoryAsync(index - 1)) {
                    return false;
                }
            }

            _history.RemoveRange(index, _history.Count - index);
            _currentIndex = Math.Min(_currentIndex, _history.Count - 1);
            ValidateIndex();
            UpdateCurrentStateMarkers();
            OnHistoryChanged();
            return true;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to delete history from index {Index}", index);
            _currentIndex = Math.Max(-1, _history.Count - 1);
            UpdateCurrentStateMarkers();
            OnHistoryChanged();
            return false;
        }
    }

    public List<HistoryListItem> GetHistoryList() {
        var items = new List<HistoryListItem> {
            new HistoryListItem {
                Index = -1,
                Description = "Original Document (Opened)",
                Timestamp = DateTime.MinValue,
                IsCurrent = _currentIndex == -1,
                IsSnapshot = false
            }
        };

        for (int i = 0; i < _history.Count; i++) {
            var entry = _history[i];
            items.Add(new HistoryListItem {
                Index = i,
                Description = entry.Description,
                Timestamp = entry.Timestamp,
                IsCurrent = _currentIndex == i,
                IsSnapshot = false
            });
        }

        return items;
    }

    public void Clear() {
        _history.Clear();
        _currentIndex = -1;
        UpdateCurrentStateMarkers();
        OnHistoryChanged();
    }

    public void ResetToBase() {
        _currentIndex = -1;
        ValidateIndex();
        UpdateCurrentStateMarkers();
        // Do not invoke OnHistoryChanged here, as state is set externally
    }

    private void TrimHistory() {
        try {
            while (_history.Count > _maxHistorySize && _history.Count >= 2) {
                var oldest = _history[0];
                var next = _history[1];
                var mergedCommand = MergeCommands(oldest.Command, next.Command);
                var newEntry = new HistoryEntry(mergedCommand) {
                    Description = next.Description,
                    Timestamp = next.Timestamp,
                    AffectedDocumentIds = next.AffectedDocumentIds // Preserve document IDs
                };
                _history[1] = newEntry;
                _history.RemoveAt(0);
                _currentIndex--;
            }
            ValidateIndex();
            UpdateCurrentStateMarkers();
            OnHistoryChanged();
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Failed to trim history");
            _currentIndex = Math.Max(-1, _history.Count - 1);
            UpdateCurrentStateMarkers();
            OnHistoryChanged();
        }
    }

    private ICommand MergeCommands(ICommand first, ICommand second) {
        var composite = new CompositeCommand();
        if (first is CompositeCommand c1) {
            composite.Commands.AddRange(c1.Commands);
        }
        else {
            composite.Commands.Add(first);
        }
        if (second is CompositeCommand c2) {
            composite.Commands.AddRange(c2.Commands);
        }
        else {
            composite.Commands.Add(second);
        }
        // Merge AffectedDocumentIds
        composite.AffectedDocumentIds = new List<string>();
        if (first.AffectedDocumentIds != null) composite.AffectedDocumentIds.AddRange(first.AffectedDocumentIds);
        if (second.AffectedDocumentIds != null) composite.AffectedDocumentIds.AddRange(second.AffectedDocumentIds);
        composite.AffectedDocumentIds = composite.AffectedDocumentIds.Distinct().ToList();
        return composite;
    }

    private void ValidateIndex() {
        if (_history.Count == 0) {
            _currentIndex = -1;
        }
        else {
            _currentIndex = Math.Clamp(_currentIndex, -1, _history.Count - 1);
        }
    }

    private void UpdateCurrentStateMarkers() {
        for (int i = 0; i < _history.Count; i++) {
            _history[i].IsCurrentState = i == _currentIndex;
        }
    }

    private void OnHistoryChanged() {
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }
}