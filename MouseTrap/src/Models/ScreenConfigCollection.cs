using Microsoft.Win32;


namespace MouseTrap.Models;

[Serializable]
public class ScreenConfigCollection : List<ScreenConfig> {
    public ScreenConfigCollection()
    {
    }

    public ScreenConfigCollection(IEnumerable<ScreenConfig> enumerable) : base(enumerable)
    {
    }


    #region Reload on configuration change

    static ScreenConfigCollection()
    {
        if (OperatingSystem.IsWindows()) {
            SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
        }
    }

    public static event ScreenConfigChanged? OnChanged;

    private static void DisplaySettingsChanged(object? sender, EventArgs eventArgs)
    {
        var configCollection = Load();
        OnChanged?.Invoke(configCollection);
    }

    #endregion


    public void Save()
    {
        SettingsFile.Save(this);
    }

    public static ScreenConfigCollection Load()
    {
        var loaded = SettingsFile.Load<ScreenConfigCollection>();
        var screens = Screen.AllScreens;
        var currentMonitorIds = screens.Select(s => s.DeviceMonitorId()).ToArray();

        // old ScreenId -> current Screen.AllScreens index, preferring a match by the monitor's own
        // stable identity over the (possibly now-shuffled) index the config was originally saved
        // under. Configs saved before MonitorId existed, or whose monitor's id didn't match any
        // currently connected screen, fall back to the previous purely positional behavior.
        var idMap = new Dictionary<int, int>();
        var usedCurrentIndexes = new HashSet<int>();

        foreach (var old in loaded) {
            if (old.MonitorId == null) continue;

            var newIndex = Array.IndexOf(currentMonitorIds, old.MonitorId);
            if (newIndex >= 0 && usedCurrentIndexes.Add(newIndex)) {
                idMap[old.ScreenId] = newIndex;
            }
        }

        foreach (var old in loaded) {
            if (idMap.ContainsKey(old.ScreenId)) continue;
            if (old.ScreenId < screens.Length && usedCurrentIndexes.Add(old.ScreenId)) {
                idMap[old.ScreenId] = old.ScreenId;
            }
        }

        // resolve every old entry's new index against the *original* (still-untouched) ScreenId
        // values before reassigning any of them - idMap can contain cycles (e.g. old 0 -> new 3 and
        // old 3 -> new 0), and mutating ScreenId while still looking entries up by it would let an
        // already-relabeled entry get matched again under its new, coincidentally-old-looking id
        var matches = new ScreenConfig?[screens.Length];
        for (var i = 0; i < screens.Length; i++) {
            matches[i] = loaded.FirstOrDefault(x => idMap.TryGetValue(x.ScreenId, out var mapped) && mapped == i);
        }

        var obj = new ScreenConfigCollection();
        for (var i = 0; i < screens.Length; i++) {
            var config = matches[i] ?? new ScreenConfig();

            config.ScreenId = i;
            config.MonitorId = currentMonitorIds[i];
            config.Name = screens[i].DeviceFriendlyName();
            config.Bounds = screens[i].Bounds;
            config.Primary = screens[i].Primary;

            RemapOrDropBridges(config.TopBridges, idMap);
            RemapOrDropBridges(config.LeftBridges, idMap);
            RemapOrDropBridges(config.RightBridges, idMap);
            RemapOrDropBridges(config.BottomBridges, idMap);

            obj.Add(config);
        }

        return obj;
    }

    private static void RemapOrDropBridges(List<Bridge> bridges, Dictionary<int, int> idMap)
    {
        for (var i = bridges.Count - 1; i >= 0; i--) {
            // the target screen is no longer connected (or never matched) - drop a bridge to nowhere
            if (!idMap.TryGetValue(bridges[i].TargetScreenId, out var newTargetId)) {
                bridges.RemoveAt(i);
            }
            else {
                bridges[i].TargetScreenId = newTargetId;
            }
        }
    }
}

public delegate void ScreenConfigChanged(ScreenConfigCollection config);
