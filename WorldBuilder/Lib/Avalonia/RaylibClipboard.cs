using System;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Raylib_cs;
using IDataObject = Avalonia.Input.IDataObject;

namespace WorldBuilder.Lib.Avalonia;
internal sealed class RaylibClipboard : IClipboard {
    public Task<string?> GetTextAsync() {
        return Task.FromResult<string?>(Raylib.GetClipboardText_());
    }

	public Task SetTextAsync(string? text) {
        Raylib.SetClipboardText(text ?? String.Empty);
		return Task.CompletedTask;
	}

	public Task ClearAsync()
		=> SetTextAsync(String.Empty);

	public Task<string[]> GetFormatsAsync()
		=> Task.FromResult(Array.Empty<string>());

	public Task<object?> GetDataAsync(string format)
		=> Task.FromResult<object?>(null);

    public Task SetDataObjectAsync(global::Avalonia.Input.IDataObject data)
        => Task.CompletedTask;

    public Task FlushAsync() {
        return Task.CompletedTask;
    }

    public Task<IDataObject?> TryGetInProcessDataObjectAsync() {
        return Task.FromResult<IDataObject?>(null);
    }
}
