using Microsoft.UI.Xaml;
#if HAS_UNO
using Uno.Resizetizer;
#endif

namespace PickMatch;

public partial class App : Application
{
    Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
#if HAS_UNO
        _window.SetWindowIcon();
#endif
        _window.Activate();
    }
}
