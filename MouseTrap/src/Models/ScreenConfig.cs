using System.Text.Json.Serialization;


namespace MouseTrap.Models;

[Serializable]
public class ScreenConfig {
    public int ScreenId { get; set; }
    [JsonIgnore]
    public string ScreenNum => (ScreenId + 1).ToString();

    public string? Name { get; set; }

    // EDID/device-path based identity of the physical monitor, used to keep ScreenId (and the
    // bridges that reference it) pointing at the right monitor even if Windows changes the
    // Screen.AllScreens enumeration order; null for configs saved before this existed
    public string? MonitorId { get; set; }

    public bool Primary { get; set; }

    [JsonIgnore]
    public bool HasBridges => TopBridges.Count > 0 || LeftBridges.Count > 0 || RightBridges.Count > 0 || BottomBridges.Count > 0;

    public List<Bridge> TopBridges { get; set; } = new();
    public List<Bridge> LeftBridges { get; set; } = new();
    public List<Bridge> RightBridges { get; set; } = new();
    public List<Bridge> BottomBridges { get; set; } = new();

    // kept only to migrate settings files written before multiple bridges per edge were supported
    [JsonPropertyName("TopBridge")]
    public Bridge? LegacyTopBridge { get => null; set { if (value != null) TopBridges.Add(value); } }
    [JsonPropertyName("LeftBridge")]
    public Bridge? LegacyLeftBridge { get => null; set { if (value != null) LeftBridges.Add(value); } }
    [JsonPropertyName("RightBridge")]
    public Bridge? LegacyRightBridge { get => null; set { if (value != null) RightBridges.Add(value); } }
    [JsonPropertyName("BottomBridge")]
    public Bridge? LegacyBottomBridge { get => null; set { if (value != null) BottomBridges.Add(value); } }

    public Rectangle Bounds;


    private const int Space = 2;

    [JsonIgnore]
    public IEnumerable<(Bridge Bridge, Rectangle HotSpace)> RightHotSpaces => RightBridges.Select(b => (b, HotSpaceFor(Edge.Right, b)));
    [JsonIgnore]
    public IEnumerable<(Bridge Bridge, Rectangle HotSpace)> LeftHotSpaces => LeftBridges.Select(b => (b, HotSpaceFor(Edge.Left, b)));
    [JsonIgnore]
    public IEnumerable<(Bridge Bridge, Rectangle HotSpace)> TopHotSpaces => TopBridges.Select(b => (b, HotSpaceFor(Edge.Top, b)));
    [JsonIgnore]
    public IEnumerable<(Bridge Bridge, Rectangle HotSpace)> BottomHotSpaces => BottomBridges.Select(b => (b, HotSpaceFor(Edge.Bottom, b)));

    public Rectangle HotSpaceFor(Edge edge, Bridge bridge)
    {
        return edge switch {
            Edge.Right => new Rectangle(
                Bounds.X + Bounds.Width - Space,
                Bounds.Y + bridge.TopOffset,
                Space,
                Bounds.Height - bridge.TopOffset - bridge.BottomOffset
            ),
            Edge.Left => new Rectangle(
                Bounds.X,
                Bounds.Y + bridge.TopOffset,
                Space,
                Bounds.Height - bridge.TopOffset - bridge.BottomOffset
            ),
            Edge.Top => new Rectangle(
                Bounds.X + bridge.TopOffset,
                Bounds.Y,
                Bounds.Width - bridge.TopOffset - bridge.BottomOffset,
                Space
            ),
            Edge.Bottom => new Rectangle(
                Bounds.X + bridge.TopOffset,
                Bounds.Y + Bounds.Height - Space,
                Bounds.Width - bridge.TopOffset - bridge.BottomOffset,
                Space
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(edge))
        };
    }

    public List<Bridge> BridgesFor(Edge edge)
    {
        return edge switch {
            Edge.Top => TopBridges,
            Edge.Left => LeftBridges,
            Edge.Right => RightBridges,
            Edge.Bottom => BottomBridges,
            _ => throw new ArgumentOutOfRangeException(nameof(edge))
        };
    }
}

[Serializable]
public class Bridge {
    // pairs a bridge with its mirrored counterpart on the target screen so that removing
    // one side can find and remove the other, even across app restarts
    public Guid Id { get; set; } = Guid.NewGuid();

    public int TopOffset { get; set; }
    public int BottomOffset { get; set; }
    public int TargetScreenId { get; set; }
}

public enum Edge {
    Top,
    Left,
    Right,
    Bottom
}
