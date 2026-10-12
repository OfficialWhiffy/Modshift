using System.Diagnostics;

namespace TruckModsListUpdater.App.Services;

/// <summary>Opens web links in the default browser. Mod descriptions are untrusted, so only http(s) is allowed.</summary>
public static class ExternalLinks
{
    public static bool TryOpen(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return false;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }
}
