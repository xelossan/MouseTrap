using System.ComponentModel;
using MouseTrap.Models;
using MouseTrap.Native;


namespace MouseTrap.Service;

public class MouseBridgeService : IService {
    private ScreenConfigCollection _screens;

    public MouseBridgeService()
    {
        _screens = ScreenConfigCollection.Load();
        ScreenConfigCollection.OnChanged += config => {
            _screens = config;
        };
    }

    public MouseBridgeService(ScreenConfigCollection screens)
    {
        _screens = screens;
    }

    public void OnStart()
    {
    }

    private int _errorCount = 0;

    public void Run(CancellationToken token)
    {
        try {
            Loop(token);
        }
        catch (Win32Exception) {
            if (token.IsCancellationRequested) {
                return;
            }

            _errorCount++;
            if (_errorCount < 5) {
                Run(token);
            }
            else {
                throw;
            }
        }
    }

    public void OnExit()
    {
        MouseTrapClear();
    }


    private void Loop(CancellationToken token)
    {
        while (!token.IsCancellationRequested) {
            // on win-logon etc..
            if (!Mouse.IsInputDesktop()) {
                MouseTrapClear();
                Thread.Sleep(1);
                continue;
            }

            var position = GetPosition();

            var current = _screens.FirstOrDefault(_ => _.Bounds.Contains(position));
            if (current != null && current.HasBridges) {
                MouseTrap(current);

                var direction = GetDirection(in position);

                // ==>
                if (direction.HasFlag(Direction.ToRight)) {
                    foreach (var (bridge, hotspace) in current.RightHotSpaces) {
                        if (!hotspace.Contains(position)) continue;

                        var targetScreen = _screens.FirstOrDefault(_ => _.ScreenId == bridge.TargetScreenId);
                        var targetBridge = FindReciprocalBridge(targetScreen, Edge.Left, current.ScreenId);
                        if (targetScreen != null && targetBridge != null) {
                            var target = targetScreen.HotSpaceFor(Edge.Left, targetBridge);
                            MouseTrapClear();

                            var newY = MapY(position.Y, in hotspace, in target);
                            MouseMove(in current.Bounds, in targetScreen.Bounds, (target.X + target.Width + 1), newY);
                        }

                        break;
                    }
                }

                // <==
                if (direction.HasFlag(Direction.ToLeft)) {
                    foreach (var (bridge, hotspace) in current.LeftHotSpaces) {
                        if (!hotspace.Contains(position)) continue;

                        var targetScreen = _screens.FirstOrDefault(_ => _.ScreenId == bridge.TargetScreenId);
                        var targetBridge = FindReciprocalBridge(targetScreen, Edge.Right, current.ScreenId);
                        if (targetScreen != null && targetBridge != null) {
                            var target = targetScreen.HotSpaceFor(Edge.Right, targetBridge);
                            MouseTrapClear();

                            var newY = MapY(position.Y, in hotspace, in target);
                            MouseMove(in current.Bounds, in targetScreen.Bounds, (target.X - 1), newY);
                        }

                        break;
                    }
                }

                // ^
                if (direction.HasFlag(Direction.ToTop)) {
                    foreach (var (bridge, hotspace) in current.TopHotSpaces) {
                        if (!hotspace.Contains(position)) continue;

                        var targetScreen = _screens.FirstOrDefault(_ => _.ScreenId == bridge.TargetScreenId);
                        var targetBridge = FindReciprocalBridge(targetScreen, Edge.Bottom, current.ScreenId);
                        if (targetScreen != null && targetBridge != null) {
                            var target = targetScreen.HotSpaceFor(Edge.Bottom, targetBridge);
                            MouseTrapClear();

                            var newX = MapX(position.X, in hotspace, in target);
                            MouseMove(in current.Bounds, in targetScreen.Bounds, newX, (target.Y - 1));
                        }

                        break;
                    }
                }

                // v
                if (direction.HasFlag(Direction.ToBottom)) {
                    foreach (var (bridge, hotspace) in current.BottomHotSpaces) {
                        if (!hotspace.Contains(position)) continue;

                        var targetScreen = _screens.FirstOrDefault(_ => _.ScreenId == bridge.TargetScreenId);
                        var targetBridge = FindReciprocalBridge(targetScreen, Edge.Top, current.ScreenId);
                        if (targetScreen != null && targetBridge != null) {
                            var target = targetScreen.HotSpaceFor(Edge.Top, targetBridge);
                            MouseTrapClear();

                            var newX = MapX(position.X, in hotspace, in target);
                            MouseMove(in current.Bounds, in targetScreen.Bounds, newX, (target.Y + target.Height + 1));
                        }

                        break;
                    }
                }
            }

            Thread.Sleep(1);
        }
    }


    private Point GetPosition()
    {
        if (!Mouse.TryGetPosition(out var pos)) {
            return Point.Empty;
        }

        return pos;
    }

    // several bridges can now share the same edge, so pick the one that actually points back
    // to the screen we're coming from; fall back to the first one for older, single-bridge configs
    private static Bridge? FindReciprocalBridge(ScreenConfig? targetScreen, Edge edge, int sourceScreenId)
    {
        if (targetScreen == null) return null;

        var bridges = targetScreen.BridgesFor(edge);
        return bridges.FirstOrDefault(b => b.TargetScreenId == sourceScreenId) ?? bridges.FirstOrDefault();
    }


    private int _posOldx;
    private int _posOldy;

    private Direction GetDirection(in Point pos)
    {
        var ret = Direction.None;
        if (_posOldx < pos.X) {
            _posOldx = pos.X;

            ret |= Direction.ToRight;
        }

        if (_posOldx > pos.X) {
            _posOldx = pos.X;

            ret |= Direction.ToLeft;
        }

        if (_posOldy < pos.Y) {
            _posOldy = pos.Y;

            ret |= Direction.ToBottom;
        }

        if (_posOldy > pos.Y) {
            _posOldy = pos.Y;

            ret |= Direction.ToTop;
        }

        return ret;
    }

    private static int MapY(int y, in Rectangle src, in Rectangle dst)
    {
        var percent = (y - src.Y) / (float) src.Height;
        var newY = (int) (dst.Height * percent) + dst.Y;
        return newY;
    }

    private static int MapX(int x, in Rectangle src, in Rectangle dst)
    {
        var percent = (x - src.X) / (float) src.Width;
        var newX = (int) (dst.Width * percent) + dst.X;
        return newX;
    }

    private int _activeTrap = -1;

    private void MouseTrap(ScreenConfig config)
    {
        if (_activeTrap != config.ScreenId) {
            Mouse.SetClip(in config.Bounds);
            _activeTrap = config.ScreenId;
        }
        else {
            var clip = Mouse.GetClip();
            if (clip != config.Bounds) {
                Mouse.SetClip(in config.Bounds);
            }
        }
    }

    private void MouseTrapClear()
    {
        if (_activeTrap != -1) {
            Mouse.ClearClip();
            _activeTrap = -1;
        }
    }

    private void MouseMove(in Rectangle srcBounds, in Rectangle targetBounds, int x, int y)
    {
        // Mouse.SwitchToInputDesktop();

        Mouse.MoveCursor(x, y);

        var pos = GetPosition();
        if (pos.X != x || pos.Y != y) {
            for (var i = 0; i < 3; i++) {
                Mouse.MoveCursor(x, y);

                pos = GetPosition();
                if (pos.X == x && pos.Y == y) {
                    return;
                }
            }
        }
    }
}

[Flags]
internal enum Direction : byte {
    None = 0x00,
    ToLeft = 0x01,
    ToRight = 0x02,
    ToTop = 0x04,
    ToBottom = 0x08,
}
