using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;

namespace JackOfAll.UserControls.Services;

public enum BluetoothRadioState { Unknown, NoAdapter, Off, On }

public sealed record BluetoothRadioInfo(BluetoothRadioState State, bool LowEnergy, string Address);

public sealed record PairedBluetoothDevice(string Name, string Category, bool Connected, int? BatteryPercent, bool LowEnergy);

/// <summary>A device heard advertising over Bluetooth LE, with a short signal history for the finder.</summary>
public sealed class NearbyBluetoothDevice
{
    private const int HistoryCap = 120;
    private readonly Queue<float> _history = new();

    public ulong Address { get; init; }
    public string Name { get; set; } = "";
    public string Maker { get; set; } = "";
    public short? TxPower { get; set; }
    public double SmoothedRssi { get; private set; } = double.NaN;
    public DateTime LastSeen { get; private set; }

    public string AddressText => string.Join(":", BitConverter.GetBytes(Address).Take(6).Reverse().Select(b => b.ToString("x2")));
    public string Shown => Name.Length > 0 ? Name : Maker.Length > 0 ? $"{Maker} device" : "Unnamed device";

    /// <summary>
    /// Rough distance from the log-distance path-loss model. Walls and bodies easily double it,
    /// so it's only good for "in this room" versus "next door".
    /// </summary>
    public double EstimatedMeters
    {
        get
        {
            if (double.IsNaN(SmoothedRssi)) return double.NaN;
            var reference = TxPower is { } tx and < 0 ? tx : -59; // typical RSSI at 1 m when the device doesn't say
            return Math.Pow(10, (reference - SmoothedRssi) / (10 * 2.2));
        }
    }

    public float[] History() { lock (_history) return [.. _history]; }

    internal void Observe(short rssi)
    {
        SmoothedRssi = double.IsNaN(SmoothedRssi) ? rssi : SmoothedRssi + (rssi - SmoothedRssi) * 0.35;
        LastSeen = DateTime.Now;
        lock (_history)
        {
            _history.Enqueue((float)SmoothedRssi);
            while (_history.Count > HistoryCap) _history.Dequeue();
        }
    }
}

public sealed class BluetoothService : IDisposable
{
    // DEVPKEY_Bluetooth_Battery: what Windows shows as "battery" in Settings for headsets and mice
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string IsConnectedKey = "System.Devices.Aep.IsConnected";
    private const string ContainerKey = "System.Devices.Aep.ContainerId";
    private const string CodMajorKey = "System.Devices.Aep.Bluetooth.Cod.Major";
    private static readonly TimeSpan Forget = TimeSpan.FromSeconds(30);

    private readonly Dictionary<ulong, NearbyBluetoothDevice> _nearby = [];
    private BluetoothLEAdvertisementWatcher? _watcher;

    public bool Scanning => _watcher?.Status == BluetoothLEAdvertisementWatcherStatus.Started;
    public string ScanError { get; private set; } = "";

    public static async Task<BluetoothRadioInfo> GetRadioAsync()
    {
        try
        {
            var adapter = await BluetoothAdapter.GetDefaultAsync();
            if (adapter is null) return new(BluetoothRadioState.NoAdapter, false, "");
            var radio = await adapter.GetRadioAsync();
            var state = radio?.State switch
            {
                RadioState.On => BluetoothRadioState.On,
                RadioState.Off or RadioState.Disabled => BluetoothRadioState.Off,
                _ => BluetoothRadioState.Unknown,
            };
            var addr = string.Join(":", BitConverter.GetBytes(adapter.BluetoothAddress).Take(6).Reverse().Select(b => b.ToString("x2")));
            return new(state, adapter.IsLowEnergySupported, addr);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return new(BluetoothRadioState.Unknown, false, "");
        }
    }

    public static async Task<List<PairedBluetoothDevice>> GetPairedAsync()
    {
        var battery = await BatteryByContainerAsync();
        var result = new List<PairedBluetoothDevice>();
        string[] props = [IsConnectedKey, ContainerKey, CodMajorKey];
        foreach (var (selector, le) in new[]
                 {
                     (BluetoothDevice.GetDeviceSelectorFromPairingState(true), false),
                     (BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), true),
                 })
        {
            DeviceInformationCollection found;
            try { found = await DeviceInformation.FindAllAsync(selector, props, DeviceInformationKind.AssociationEndpoint); }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { continue; }

            foreach (var d in found)
            {
                if (string.IsNullOrWhiteSpace(d.Name)) continue;
                var connected = d.Properties.TryGetValue(IsConnectedKey, out var c) && c is true;
                var container = d.Properties.TryGetValue(ContainerKey, out var g) && g is Guid id ? id : Guid.Empty;
                // the class-of-device value arrives as a byte on some drivers and a wider integer on others
                var major = d.Properties.TryGetValue(CodMajorKey, out var m) && m is IConvertible cv ? (byte)Convert.ToInt32(cv) : (byte)0;
                int? level = battery.TryGetValue(container, out var lvl) ? lvl : null;
                result.Add(new PairedBluetoothDevice(d.Name, Category(major, d.Name, le), connected, level, le));
            }
        }
        // a dual-mode device (most headphones) shows up in both lists; keep one row per name
        return result
            .GroupBy(r => r.Name)
            .Select(grp => grp.OrderByDescending(r => r.Connected).ThenByDescending(r => r.BatteryPercent.HasValue).First() with
            {
                Connected = grp.Any(r => r.Connected),
                BatteryPercent = grp.Select(r => r.BatteryPercent).FirstOrDefault(v => v.HasValue),
            })
            .OrderByDescending(r => r.Connected)
            .ThenBy(r => r.Name)
            .ToList();
    }

    private static async Task<Dictionary<Guid, int>> BatteryByContainerAsync()
    {
        var map = new Dictionary<Guid, int>();
        try
        {
            var devices = await DeviceInformation.FindAllAsync(
                "System.Devices.Present:=System.StructuredQueryType.Boolean#True",
                [BatteryKey, "System.Devices.ContainerId"], DeviceInformationKind.Device);
            foreach (var d in devices)
            {
                if (!d.Properties.TryGetValue(BatteryKey, out var v) || v is not byte level) continue;
                if (d.Properties.TryGetValue("System.Devices.ContainerId", out var c) && c is Guid id) map[id] = level;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
        }
        return map;
    }

    // Bluetooth "class of device" major numbers; LE devices don't carry one, so fall back to the name.
    private static string Category(byte major, string name, bool le)
    {
        var fromCod = major switch
        {
            1 => "Computer",
            2 => "Phone",
            4 => "Audio",
            5 => "Input device",
            6 => "Imaging",
            7 => "Wearable",
            8 => "Toy",
            9 => "Health",
            _ => "",
        };
        if (fromCod.Length > 0) return fromCod;
        var n = name.ToLowerInvariant();
        if (n.Contains("buds") || n.Contains("pods") || n.Contains("bullets") || n.Contains("headphone") || n.Contains("headset")
            || n.Contains("speaker") || n.Contains("audio")) return "Audio";
        if (n.Contains("mouse") || n.Contains("keyboard") || n.Contains("controller") || n.Contains("pen")) return "Input device";
        if (n.Contains("watch") || n.Contains("band")) return "Wearable";
        return le ? "Low energy device" : "Device";
    }

    // ---------------------------------------------------------------- live LE scan

    public void StartScan()
    {
        if (Scanning) return;
        ScanError = "";
        _watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        _watcher.Received += OnReceived;
        _watcher.Stopped += (_, e) =>
        {
            if (e.Error != BluetoothError.Success) ScanError = e.Error == BluetoothError.RadioNotAvailable ? "Bluetooth is off." : $"Scan stopped ({e.Error}).";
        };
        try { _watcher.Start(); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            ScanError = "Couldn't start a Bluetooth scan: " + ex.Message;
        }
    }

    public void StopScan()
    {
        if (_watcher is null) return;
        _watcher.Received -= OnReceived;
        if (_watcher.Status is BluetoothLEAdvertisementWatcherStatus.Started or BluetoothLEAdvertisementWatcherStatus.Created) _watcher.Stop();
        _watcher = null;
    }

    public NearbyBluetoothDevice? Find(ulong address)
    {
        lock (_nearby) return _nearby.GetValueOrDefault(address);
    }

    /// <summary>Devices heard in the last 30 seconds, strongest first.</summary>
    public List<NearbyBluetoothDevice> Nearby()
    {
        lock (_nearby)
        {
            var cutoff = DateTime.Now - Forget;
            foreach (var stale in _nearby.Values.Where(d => d.LastSeen < cutoff).Select(d => d.Address).ToList()) _nearby.Remove(stale);
            return _nearby.Values.OrderByDescending(d => d.SmoothedRssi).ToList();
        }
    }

    private void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs e)
    {
        NearbyBluetoothDevice d;
        lock (_nearby)
        {
            if (!_nearby.TryGetValue(e.BluetoothAddress, out d!))
                _nearby[e.BluetoothAddress] = d = new NearbyBluetoothDevice { Address = e.BluetoothAddress };
        }
        var adv = e.Advertisement;
        if (!string.IsNullOrWhiteSpace(adv.LocalName)) d.Name = adv.LocalName.Trim();
        if (adv.ManufacturerData.Count > 0 && d.Maker.Length == 0) d.Maker = Maker(adv.ManufacturerData[0].CompanyId);
        if (e.TransmitPowerLevelInDBm is { } tx) d.TxPower = tx;
        d.Observe(e.RawSignalStrengthInDBm);
    }

    // The most common Bluetooth SIG company identifiers seen around homes and offices.
    private static string Maker(ushort companyId) => companyId switch
    {
        0x004C => "Apple",
        0x0006 => "Microsoft",
        0x0075 => "Samsung",
        0x00E0 => "Google",
        0x0087 => "Garmin",
        0x038F => "Xiaomi",
        0x027D => "Huawei",
        0x0157 => "Huami",
        0x0059 => "Nordic",
        0x000F => "Broadcom",
        0x0171 => "Amazon",
        0x02E5 => "Espressif",
        0x0499 => "Ruuvi",
        0x012D => "Sony",
        _ => "",
    };

    public void Dispose() => StopScan();
}
