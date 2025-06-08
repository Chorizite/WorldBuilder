using System;
using Avalonia.Rendering;

namespace WorldBuilder.Lib.Avalonia;

internal sealed class ManualRenderTimer : IRenderTimer {

	public event Action<TimeSpan>? Tick;

	bool IRenderTimer.RunsInBackground
		=> false;

	public void TriggerTick(TimeSpan elapsed)
		=> Tick?.Invoke(elapsed);

}
