namespace MementoMori.Exporter.Android.Services;

public static class ExportSelection
{
    // Every launch starts with the complete existing export contract, including gacha.
    // Return a copy so UI edits cannot alter the default for later launches.
    public static string[] DefaultSections() => MobileSnapshot.Sections.ToArray();
}
