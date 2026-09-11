namespace MementoMori.Exporter.Android;

public sealed class App(MainPage page) : Application
{
    protected override Window CreateWindow(IActivationState? activationState)
    {
        UserAppTheme = AppTheme.Light;
        return new Window(new NavigationPage(page) { BarBackgroundColor = Color.FromArgb("#F4F6FB"), BarTextColor = Color.FromArgb("#15233B") });
    }
}
