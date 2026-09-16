using System.ComponentModel;
using System.Diagnostics;
using MouseTrap.Models;
using MouseTrap.Native;


namespace MouseTrap.Service;

public class MouseBridgeDiagnosticService : IService {
    private ScreenConfigCollection _screens;
    private readonly Action<string> _log;
    private volatile uint _count = 0;

    public MouseBridgeDiagnosticService(ScreenConfigCollection screens, Action<string> log)
    {
        _screens = screens;
        ScreenConfigCollection.OnChanged += config => {
            _screens = config;
        };

        var sw = new Stopwatch();
        sw.Start();

        _log = msg => {
            msg = $"{sw.Elapsed,-15:g} {Interlocked.Increment(ref _count),11} [{DirectionArrow(_direction)}]  Cursor({_position.X,4}, {_position.Y,4})  {msg}";
            if (_count == uint.MaxValue) _count = 0;

            Task.Run(() => log(msg));
        };

        static char DirectionArrow(Direction d)
        {
            return d switch {
                Direction.ToTop => '↑',
                Direction.ToBottom => '↓',
                Direction.ToLeft => '←',
                Direction.ToRight => '→',
                Direction.ToTop | Direction.ToLeft => '↖',
                Direction.ToTop | Direction.ToRight => '↗',
                Direction.ToBottom | Direction.ToLeft => '↙',
                Direction.ToBottom | Direction.ToRight => '↘',
                _ => ' '
            };
        }
    }


    public void OnStart()
    {
    }

    private int _errorCount = 0;

    public void Run(CancellationToken token)
    {
        _log("run");
        try {
            Loop(token);
        }
        catch (Win32Exception e) {
            if (token.IsCancellationRequested) {
                return;
            }

            _log($"\r\n{e}\r\n");

            _errorCount++;
            if (_errorCount < 5) {
                _log("rerun");
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

                            _log($"    Δ src-dst: {target.X - position.X} - {target.Width}  smallJump: {Math.Abs(target.X - position.X) <= target.Width + 1}");

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

                            _log($"    Δ src-dst: {target.X - position.X} - {target.Width}  smallJump: {Math.Abs(target.X - position.X) <= target.Width + 1}");

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

                            _log($"    Δ src-dst: {target.Y - position.Y} - {target.Height}  smallJump: {Math.Abs(target.Y - position.Y) <= target.Height + 1}");

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

                            _log($"    Δ src-dst: {target.Y - position.Y} - {target.Height}  smallJump: {Math.Abs(target.Y - position.Y) <= target.Height + 1}");

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

    private Point _position;

    private Point GetPosition()
    {
        if (!Mouse.TryGetPosition(out var pos)) {
            _log($"Failed to receive Cursor Position");
            return Point.Empty;
        }

        return _position = pos;
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
    private Direction _direction;

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

        return _direction = ret;
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
            _log($"MouseTrap({config.Bounds})   -- IsInputDesktop: {Mouse.IsInputDesktop()}");
            Mouse.SetClip(in config.Bounds);
            _activeTrap = config.ScreenId;
        }
        else {
            var clip = Mouse.GetClip();
            if (clip != config.Bounds) {
                _log($"MouseTrap({config.Bounds}) system had wrong clip: {clip}   -- IsInputDesktop: {Mouse.IsInputDesktop()}");
                Mouse.SetClip(in config.Bounds);
            }
        }
    }

    private void MouseTrapClear()
    {
        if (_activeTrap != -1) {
            _log($"MouseTrapClear({_activeTrap})");
            Mouse.ClearClip();
            _activeTrap = -1;
        }
    }

    private void MouseMove(in Rectangle srcBounds, in Rectangle targetBounds, int x, int y)
    {
        _log($"    Move To Screen -> {targetBounds}");

        // TODO enable SwitchToInputDesktop for setup screen!
        // _log($"    SwitchToInputDesktop");
        // Mouse.SwitchToInputDesktop();

        _log($"    MouseMove     ({x,4}, {y,4})");
        Mouse.MoveCursor(x, y);

        var pos = GetPosition();
        if (pos.X != x || pos.Y != y) {
            for (var i = 0; i < 3; i++) {
                _log($"    Move Failed ! ({pos.X,4}, {pos.Y,4}) - Retry");
                Mouse.MoveCursor(x, y);

                pos = GetPosition();
                if (pos.X == x && pos.Y == y) {
                    return;
                }
            }

            _log($"    Move Failed ! ({pos.X,4}, {pos.Y,4}) - No Retry");
        }
    }
}
