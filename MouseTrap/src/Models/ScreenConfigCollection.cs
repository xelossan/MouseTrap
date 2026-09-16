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

        var obj = new ScreenConfigCollection();
        for (var i = 0; i < screens.Length; i++) {
            var config = loaded.FirstOrDefault(x => x.ScreenId == i) ?? new ScreenConfig() { ScreenId = i };

            config.Name = screens[i].DeviceFriendlyName();
            config.Bounds = screens[i].Bounds;
            config.Primary = screens[i].Primary;

            // if TargetScreen does not exist anymore
            config.TopBridges.RemoveAll(b => b.TargetScreenId >= screens.Length);
            config.LeftBridges.RemoveAll(b => b.TargetScreenId >= screens.Length);
            config.RightBridges.RemoveAll(b => b.TargetScreenId >= screens.Length);
            config.BottomBridges.RemoveAll(b => b.TargetScreenId >= screens.Length);

            obj.Add(config);
        }

        return obj;
    }
}

public delegate void ScreenConfigChanged(ScreenConfigCollection config);
