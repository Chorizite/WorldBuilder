using Avalonia;
using ReactiveUI;

namespace WorldBuilder.ViewModels;

public class MainViewModel : ViewModelBase
{
    private string greeting = "Welcome to Avalonia!";

    public string Greeting {
        get => greeting;
        set {
            this.RaiseAndSetIfChanged(ref greeting, value);
        }
    }

    public string Test { get; set; }
}
