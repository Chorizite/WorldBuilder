using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;

namespace WorldBuilder.Services;

public class LogEntry {
    public DateTime Timestamp { get; set; }
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = "Info"; // "Info", "Error", "Warning"
}

public class AppLogService {
    public ObservableCollection<LogEntry> Logs { get; } = new();
    
    // trigger the UI to automatically open the log window on errors
    public event EventHandler<LogEntry>? OnErrorLogged;

    public void LogInfo(string message) {
        AddLog(new LogEntry { Timestamp = DateTime.Now, Message = message, Type = "Info" });
    }

    public void LogError(string message) {
        var entry = new LogEntry { Timestamp = DateTime.Now, Message = message, Type = "Error" };
        AddLog(entry);
        
        Dispatcher.UIThread.Post(() => OnErrorLogged?.Invoke(this, entry));
    }

    private void AddLog(LogEntry entry) {
        Dispatcher.UIThread.Post(() => {
            Logs.Add(entry);
            // to prevent memory leaks, keep the last 1000 log entries
            if (Logs.Count > 1000) {
                Logs.RemoveAt(0);
            }
        });
    }
}