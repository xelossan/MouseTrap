using System.ComponentModel;
using MouseTrap.Models;


namespace MouseTrap.Forms;

// ReSharper disable LocalizableElement
public partial class ScreenConfigForm : Form {
    public Button BtnTop;
    public Button BtnLeft;
    public Button BtnRight;
    public Button BtnBottom;

    public List<EdgeSlider> BarsTop { get; } = new();
    public List<EdgeSlider> BarsLeft { get; } = new();
    public List<EdgeSlider> BarsRight { get; } = new();
    public List<EdgeSlider> BarsBottom { get; } = new();

    public Button ResetBtn;
    public Button TestBtn;
    public Button CancelBtn;
    public Button SaveBtn;

    public event RemoveBarEvent RemoveBar;

    public ScreenConfig Screen { get; }

#pragma warning disable CS8618 // Non-nullable variable must contain a non-null value when exiting constructor. Consider declaring it as nullable.
    public ScreenConfigForm()
#pragma warning restore CS8618
    {
        InitializeComponent();
    }

    public ScreenConfigForm(ScreenConfig screen) : this()
    {
        Screen = screen;

        this.SuspendLayout();
        this.StartPosition = FormStartPosition.Manual;
        this.Bounds = Screen.Bounds;
        this.CancelButton = CancelBtn;
        // every control here is positioned/sized from live Width/Height/Bounds at construction
        // time, not fixed designer-time coordinates, so there is nothing for WinForms' automatic
        // DPI rescaling to usefully correct - and in this mixed-DPI, multi-monitor, manually
        // positioned setup it appears to be the source of a control ending up wrongly sized
        this.AutoScaleMode = AutoScaleMode.None;
        this.KeyPreview = true;
        this.KeyDown += (sender, args) => {
            if (args.KeyCode == Keys.Escape) {
                CancelBtn.PerformClick();
            }
        };

        Panel.SuspendLayout();

        SetupButtons();
        SetupBars();
        SetupInfos();

        Panel.ResumeLayout(false);
        this.ResumeLayout(false);

        // controls laid out while the form isn't visible/settled on its real target screen yet can
        // end up with stale sizes/positions or simply miss their first paint (their Show() ->
        // Invalidate() calls before the window handle exists are no-ops) - this seems to be a race
        // with the window's own creation/show messages, since it isn't reliably reproducible.
        // Force a full relayout + recursive repaint once shown, and again one message-loop tick
        // later (BeginInvoke), so it lands after whatever queued messages caused the miss.
        this.Shown += (s, e) => {
            void RefreshLayout()
            {
                Panel.PerformLayout();
                Panel.Invalidate(true);
            }

            RefreshLayout();
            this.BeginInvoke((Action) RefreshLayout);
        };
    }


    private void SetupInfos()
    {
        var inner = new Rectangle(0, 0, Bounds.Size.Width, Bounds.Size.Height);
        inner.Inflate(-150, -150);

        var table = new TableLayoutPanel() {
            Bounds = inner,
            ColumnCount = 1,
            ColumnStyles = { new ColumnStyle(SizeType.Percent, 100) },
            RowCount = 2,
            RowStyles = {
                new RowStyle(SizeType.Percent, 60),
                new RowStyle(SizeType.Percent, 40)
            }
        };

        table.Controls.Add(new Label {
            Text = Screen.ScreenNum,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Location = Point.Empty,
            Size = new SizeF(inner.Width, inner.Height * .6f).ToSize(),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(Font.FontFamily, inner.Height * .6f * .6f, GraphicsUnit.Pixel)
        }, 0, 0);

        TestBtn = new Button {
            Text = "Test current settings",
            Anchor = AnchorStyles.None,
            Size = new Size(200, 40)
        };
        ResetBtn = new Button {
            Text = "Reset to previous",
            Anchor = AnchorStyles.None,
            Size = new Size(200, 40),
            Visible = false,
        };
        SaveBtn = new Button {
            Text = "Save",
            Anchor = AnchorStyles.None,
            Size = new Size(80, 40),
        };
        CancelBtn = new Button {
            Text = "Close",
            Anchor = AnchorStyles.None,
            Size = new Size(80, 40),
        };

        table.Controls.Add(new FlowLayoutPanel() {
            // AutoSize instead of a fixed literal: on a monitor with different DPI than the one this
            // form was first laid out for, the buttons get rescaled but a fixed Size here would not,
            // leaving the panel too small and clipping/mispositioning its (Anchor=None, centered) content
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(3),
            Anchor = AnchorStyles.None,
            Controls = { TestBtn, ResetBtn, SaveBtn, CancelBtn }
        }, 0, 1);

        Panel.Controls.Add(table);
    }


    private void SetupButtons()
    {
        var inner = new Rectangle(0, 0, Bounds.Size.Width, Bounds.Size.Height);
        inner.Inflate(-5 - 40, -5 - 40);

        BtnTop = new Button {
            Location = new Point((Width / 2) - 20, inner.X),
            Size = new Size(40, 40),
            Text = "+",
            UseVisualStyleBackColor = true
        };
        BtnTop.Click += (s, e) => AddTop();
        Panel.Controls.Add(BtnTop);

        BtnLeft = new Button {
            Location = new Point(inner.X, (Height / 2) - 20),
            Size = new Size(40, 40),
            Text = "+",
            UseVisualStyleBackColor = true
        };
        BtnLeft.Click += (s, e) => AddLeft();
        Panel.Controls.Add(BtnLeft);

        BtnRight = new Button {
            Location = new Point(inner.Width, (Height / 2) - 20),
            Size = new Size(40, 40),
            Text = "+",
            UseVisualStyleBackColor = true
        };
        BtnRight.Click += (s, e) => AddRight();
        Panel.Controls.Add(BtnRight);

        BtnBottom = new Button {
            Size = new Size(40, 40),
            Location = new Point((Width / 2) - 20, inner.Height),
            Text = "+",
            UseVisualStyleBackColor = true
        };
        BtnBottom.Click += (s, e) => AddBottom();
        Panel.Controls.Add(BtnBottom);
    }

    private void SetupBars()
    {
        // pass the persisted Bridge.Id through as the bar's BarId so that a bar loaded from disk
        // can still find and remove its mirrored counterpart on the other screen's form, even in
        // a later session where BarIds would otherwise be freshly generated on both sides
        foreach (var bridge in Screen.TopBridges) {
            AddTop(bridge.TargetScreenId, bridge.TopOffset, bridge.BottomOffset, bridge.Id);
        }

        foreach (var bridge in Screen.BottomBridges) {
            AddBottom(bridge.TargetScreenId, bridge.TopOffset, bridge.BottomOffset, bridge.Id);
        }

        foreach (var bridge in Screen.LeftBridges) {
            AddLeft(bridge.TargetScreenId, bridge.TopOffset, bridge.BottomOffset, bridge.Id);
        }

        foreach (var bridge in Screen.RightBridges) {
            AddRight(bridge.TargetScreenId, bridge.TopOffset, bridge.BottomOffset, bridge.Id);
        }
    }


    // Adds one more bridge to the given edge. When targetId is null this is a user-initiated
    // "+" click, so GetTargetScreenId is asked to pick a target and, as a side effect, creates
    // the matching bar on the opposite edge of that target screen (see ConfigFrom.GetTargetScreenId).
    public EdgeSlider AddTop(int? targetId = null, int topOffset = 0, int bottomOffset = 0, Guid? barId = null)
        => AddBar(BarsTop, BtnTop, BridgePosition.Top, LayoutStyle.Top, new Rectangle(0, 0, Width, 60), dx: 0, dy: 20, targetId, topOffset, bottomOffset, barId);

    public EdgeSlider AddBottom(int? targetId = null, int topOffset = 0, int bottomOffset = 0, Guid? barId = null)
        => AddBar(BarsBottom, BtnBottom, BridgePosition.Bottom, LayoutStyle.Bottom, new Rectangle(0, Height - 60, Width, 60), dx: 0, dy: -20, targetId, topOffset, bottomOffset, barId);

    public EdgeSlider AddLeft(int? targetId = null, int topOffset = 0, int bottomOffset = 0, Guid? barId = null)
        => AddBar(BarsLeft, BtnLeft, BridgePosition.Left, LayoutStyle.Left, new Rectangle(0, 0, 60, Height), dx: 20, dy: 0, targetId, topOffset, bottomOffset, barId);

    public EdgeSlider AddRight(int? targetId = null, int topOffset = 0, int bottomOffset = 0, Guid? barId = null)
        => AddBar(BarsRight, BtnRight, BridgePosition.Right, LayoutStyle.Right, new Rectangle(Width - 60, 0, 60, Height), dx: -20, dy: 0, targetId, topOffset, bottomOffset, barId);

    private readonly HashSet<BridgePosition> _shiftedButtons = new();

    private EdgeSlider AddBar(List<EdgeSlider> list, Button button, BridgePosition position, LayoutStyle layoutStyle, Rectangle bounds, int dx, int dy, int? targetId, int topOffset, int bottomOffset, Guid? barId)
    {
        var bar = new EdgeSlider(Panel, barId) {
            Bounds = bounds,
            LayoutStyle = layoutStyle,
            TopOffset = topOffset,
            BottomOffset = bottomOffset,
        };
        bar.RecalculateBar();

        bar.TargetScreenId = targetId ?? GetTargetScreenId(Screen.ScreenId, position, bar.BarId);

        bar.RemoveRequested += (s, e) => RemoveBarInternal(list, button, position, dx, dy, bar, notifyMirror: true);

        list.Add(bar);
        bar.Show();

        if (_shiftedButtons.Add(position)) {
            button.Location = new Point(button.Location.X + dx, button.Location.Y + dy);
        }

        return bar;
    }

    private void RemoveBarInternal(List<EdgeSlider> list, Button button, BridgePosition position, int dx, int dy, EdgeSlider bar, bool notifyMirror)
    {
        list.Remove(bar);
        bar.Dispose();

        if (list.Count == 0 && _shiftedButtons.Remove(position)) {
            button.Location = new Point(button.Location.X - dx, button.Location.Y - dy);
        }

        if (notifyMirror) {
            this.RemoveBar?.Invoke(this, position, bar.TargetScreenId, bar.BarId);
        }
    }

    public void RemoveTargetBarForPosition(BridgePosition position, Guid barId)
    {
        var (list, button, dx, dy) = ListFor(position);
        var bar = list.FirstOrDefault(_ => _.BarId == barId);
        if (bar != null) {
            RemoveBarInternal(list, button, position, dx, dy, bar, notifyMirror: false);
        }
    }

    private (List<EdgeSlider> List, Button Button, int Dx, int Dy) ListFor(BridgePosition position)
    {
        return position switch {
            BridgePosition.Top => (BarsTop, BtnTop, 0, 20),
            BridgePosition.Left => (BarsLeft, BtnLeft, 20, 0),
            BridgePosition.Right => (BarsRight, BtnRight, -20, 0),
            BridgePosition.Bottom => (BarsBottom, BtnBottom, 0, -20),
            _ => throw new ArgumentOutOfRangeException(nameof(position))
        };
    }


    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TargetScreenIdGetter GetTargetScreenId { get; set; }

    public ScreenConfig GetConfig()
    {
        Screen.TopBridges = BarsTop.Select(ToBridge).ToList();
        Screen.BottomBridges = BarsBottom.Select(ToBridge).ToList();
        Screen.LeftBridges = BarsLeft.Select(ToBridge).ToList();
        Screen.RightBridges = BarsRight.Select(ToBridge).ToList();

        return Screen;

        static Bridge ToBridge(EdgeSlider bar) => new Bridge {
            Id = bar.BarId,
            TopOffset = bar.TopOffset,
            BottomOffset = bar.BottomOffset,
            TargetScreenId = bar.TargetScreenId
        };
    }

    public void AddTargetBarForPosition(BridgePosition position, int sourceScreenId, Guid barId)
    {
        switch (position) {
            case BridgePosition.Top:
                AddBottom(sourceScreenId, barId: barId);
                break;
            case BridgePosition.Left:
                AddRight(sourceScreenId, barId: barId);
                break;
            case BridgePosition.Right:
                AddLeft(sourceScreenId, barId: barId);
                break;
            case BridgePosition.Bottom:
                AddTop(sourceScreenId, barId: barId);
                break;
        }
    }
}

public delegate void RemoveBarEvent(ScreenConfigForm sender, BridgePosition position, int targetScreenId, Guid barId);
public delegate int TargetScreenIdGetter(int sourceScreenId, BridgePosition position, Guid barId);
