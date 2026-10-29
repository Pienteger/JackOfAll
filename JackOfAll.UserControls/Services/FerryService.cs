using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using JackOfAll.Lib.Services.Ferry;

namespace JackOfAll.UserControls.Services;

/// <summary>
/// Runs Ferry's phone server while at least one Ferry tab is open, and turns server-thread events into UI-thread
/// ones. The feed lives as long as the app, so closing and reopening the tab keeps what was shared.
/// </summary>
internal sealed class FerryService
{
    private static readonly string ZipFolder = Path.Combine(Path.GetTempPath(), "JackOfAll-Ferry");
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private FerryServer? _server;
    private int _users;
    private bool _cleaned;

    public FerryHub Hub { get; } = new();
    public FerrySettings Settings { get; private set; } = new();
    public string? Error { get; private set; }
    public bool IsRunning => _server is not null;
    public string Busy { get; private set; } = "";

    /// <summary>The feed or the server state changed (UI thread).</summary>
    public event Action? Changed;

    /// <summary>A one-line message for the status bar (UI thread).</summary>
    public event Action<string>? Status;

    public FerryService()
    {
        Hub.Added += item => _dispatcher.BeginInvoke(() => OnAdded(item));
        Hub.Changed += () => _dispatcher.BeginInvoke(() => Changed?.Invoke());
    }

    public void Acquire()
    {
        if (_users++ > 0) return;
        if (!_cleaned)
        {
            _cleaned = true;
            try { Directory.Delete(ZipFolder, true); } catch { } // folders zipped in an earlier session
        }
        Settings = FerrySettings.Load();
        var server = new FerryServer(Hub, Settings);
        Error = server.Start();
        _server = Error is null ? server : null;
        Changed?.Invoke();
    }

    public void Release()
    {
        if (--_users > 0) return;
        _server?.Stop();
        _server = null;
    }

    /// <summary>The link encoded in the QR code, or "" when the server isn't running.</summary>
    public string Url(FerryAddress? address) =>
        address is null || _server is null ? "" : $"http://{address.Ip}:{_server.Port}/?k={Settings.Token}";

    public void NewLink()
    {
        Settings.Token = FerrySettings.NewToken();
        Settings.Save();
        Hub.Wake(); // phones waiting on the old link get "link expired" on their next request
    }

    public void SendText(string text) =>
        Hub.Add(new FerryItem { IsText = true, ToPhone = true, Name = "Text", Text = text, Size = Encoding.UTF8.GetByteCount(text), Device = "PC" });

    /// <summary>Shares files with the phone; folders are zipped first.</summary>
    public void Share(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (File.Exists(path))
                Hub.Add(new FerryItem { ToPhone = true, Name = Path.GetFileName(path), Path = path, Size = new FileInfo(path).Length, Device = "PC" });
            else if (Directory.Exists(path))
                _ = ZipAndShareAsync(path);
        }
    }

    private async Task ZipAndShareAsync(string folder)
    {
        var name = Path.GetFileName(folder.TrimEnd('\\'));
        Busy = $"Zipping {name}…";
        Changed?.Invoke();
        try
        {
            var zip = await Task.Run(() =>
            {
                Directory.CreateDirectory(ZipFolder);
                var target = FerryServer.UniquePath(ZipFolder, FerryServer.SafeName(name) + ".zip");
                ZipFile.CreateFromDirectory(folder, target, CompressionLevel.Fastest, includeBaseDirectory: true);
                return target;
            });
            Hub.Add(new FerryItem { ToPhone = true, Name = Path.GetFileName(zip), Path = zip, Size = new FileInfo(zip).Length, Device = "PC" });
        }
        catch (Exception exception)
        {
            Status?.Invoke($"Couldn't zip the folder: {exception.Message}");
        }
        finally
        {
            Busy = "";
            Changed?.Invoke();
        }
    }

    private void OnAdded(FerryItem item)
    {
        if (item.CanThumb) _ = Task.Run(item.EnsureThumb);
        if (!item.ToPhone)
        {
            var what = item.IsText ? "text" : item.Name;
            if (item.IsText && Settings.AutoCopy)
            {
                try
                {
                    Clipboard.SetDataObject(item.Text, true);
                    what = "text (copied to the clipboard)";
                }
                catch { /* another app holds the clipboard; the text is still in the feed */ }
            }
            Status?.Invoke($"Received {what} from {item.Device}");
        }
        Changed?.Invoke();
    }
}
