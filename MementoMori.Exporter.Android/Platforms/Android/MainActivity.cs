using global::Android.App;
using global::Android.Content;
using global::Android.Content.PM;
using Microsoft.Maui.ApplicationModel;

namespace MementoMori.Exporter.Android;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation |
    ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public sealed class MainActivity : MauiAppCompatActivity
{
    private const int CreateExportRequest = 0x4D45;
    private TaskCompletionSource<global::Android.Net.Uri?>? pendingSave;

    // ACTION_CREATE_DOCUMENT grants access to one user-chosen document, not all external storage.
    public async Task<global::Android.Net.Uri?> ChooseExportDestinationAsync(string fileName, string mimeType)
    {
        if (!MainThread.IsMainThread) throw new InvalidOperationException("The document picker requires the UI thread.");
        if (pendingSave != null) throw new InvalidOperationException("A document picker is already open.");
        if (mimeType is not ("application/json" or "application/zip") || Path.GetFileName(fileName) != fileName)
            throw new ArgumentException("Unsupported export document.");
        var completion = new TaskCompletionSource<global::Android.Net.Uri?>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingSave = completion;
        try
        {
            using var intent = new Intent(Intent.ActionCreateDocument);
            intent.AddCategory(Intent.CategoryOpenable);
            intent.SetType(mimeType);
            intent.PutExtra(Intent.ExtraTitle, fileName);
            StartActivityForResult(intent, CreateExportRequest);
            return await completion.Task;
        }
        finally
        {
            if (ReferenceEquals(pendingSave, completion)) pendingSave = null;
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != CreateExportRequest) return;
        if (resultCode != Result.Ok)
        {
            pendingSave?.TrySetResult(null);
            return;
        }
        var uri = data?.Data;
        if (uri == null || uri.Scheme != "content")
            pendingSave?.TrySetException(new IOException("The document provider returned no writable document."));
        else
            pendingSave?.TrySetResult(uri);
    }

    protected override void OnDestroy()
    {
        // Do not leave MainPage permanently busy if the Activity is destroyed while picking.
        pendingSave?.TrySetCanceled();
        pendingSave = null;
        base.OnDestroy();
    }
}
