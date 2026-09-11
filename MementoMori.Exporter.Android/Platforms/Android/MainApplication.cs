using global::Android.App;
using global::Android.Runtime;

namespace MementoMori.Exporter.Android
{
    [Application]
    public sealed class MainApplication : MauiApplication
    {
        public MainApplication(IntPtr handle, JniHandleOwnership ownership) : base(handle, ownership)
        {
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}
