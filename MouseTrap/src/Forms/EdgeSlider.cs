using System.Drawing.Drawing2D;


namespace MouseTrap.Forms;

public class EdgeSlider /*: UserControl*/ {
    public Guid BarId { get; }

    public bool Visible { get; set; }
    public Rectangle Bounds { get; set; }
    public Size Size => Bounds.Size;
    public int Width => Bounds.Size.Width;
    public int Height => Bounds.Size.Height;
    public Color BackColor { get; set; }
    public Control Form { get; }

    public event EventHandler? RemoveRequested;
    public event EventHandler? RetargetRequested;

    public LayoutStyle LayoutStyle { get; set; }
    internal Bar Bar { get; private set; } = null!;
    internal Bar FullBar => GetBar(Size, 0, 0);

    internal int BarLength => Math.Max(Width, Height);
    internal int BarSize => Math.Min(Width, Height);

    private int _topOffset;
    public int TopOffset {
        get => _topOffset;
        set => _topOffset = Math.Max(Math.Min(value, (BarLength - (BottomOffset + BarSize * 2))), 0);
    }

    private int _bottomOffset;
    public int BottomOffset {
        get => _bottomOffset;
        set => _bottomOffset = Math.Max(Math.Min(value, (BarLength - (TopOffset + BarSize * 2))), 0);
    }
    public int TargetScreenId { get; set; }

    private readonly SliderPanel _panel;
    private bool _bodyClickArmed;
    private Point _bodyClickPos;

    private readonly LayoutEventHandler _layoutHandler;
    private readonly EventHandler _hoverHandler;
    private readonly MouseEventHandler _hoverMoveHandler;
    private readonly MouseEventHandler _mouseDownHandler;
    private readonly MouseEventHandler _mouseUpHandler;
    private readonly MouseEventHandler _dragHandler;

    public EdgeSlider(SliderPanel form, Guid? barId = null)
    {
        BarId = barId ?? Guid.NewGuid();

        form.Sliders.Add(this);
        this._panel = form;
        this.Form = form;
        this.BackColor = System.Drawing.SystemColors.Control;
        //this.SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        //this.BackColor = Color.Transparent;

        _layoutHandler = (s, e) => { Bar = GetBar(Size, TopOffset, BottomOffset); };
        _hoverHandler = (s, e) => { HandleHover(); };
        _hoverMoveHandler = (s, e) => { HandleHover(); };

        _mouseDownHandler = (s, e) => {
            var clickPos = this.PointToClient(Cursor.Position);
            // another bar drawn on top of this one (same edge, overlapping ranges) owns this pixel -
            // without this, dragging/closing/retargeting here would act on both bars at once
            if (IsCoveredAt(clickPos)) return;

            if (Visible && Bar.CloseButton != Rectangle.Empty && Bar.CloseButton.Contains(clickPos)) {
                RemoveRequested?.Invoke(this, EventArgs.Empty);
                return;
            }

            Bar.Top.Active = Bar.Top.Hover;
            Bar.Bottom.Active = Bar.Bottom.Hover;

            if (Bar.Top.Active) {
                var pos = this.PointToClient(Cursor.Position);
                var loc = Bar.Top.GetBounds().Location;
                var offset = pos - new Size(loc.X, loc.Y);
                Bar.Top.CursorPos = new Size(offset.X, offset.Y);
            }

            if (Bar.Bottom.Active) {
                var pos = this.PointToClient(Cursor.Position);
                var loc = Bar.Bottom.GetBounds().Location;
                var offset = pos - new Size(loc.X, loc.Y);
                Bar.Bottom.CursorPos = Bar.Bottom.GetBounds().Size - new Size(offset.X, offset.Y);
            }

            // not resizing and not the close button - arm a plain click on the body to let the
            // user change which screen this bridge targets, without needing to delete and re-add it
            _bodyClickArmed = Visible && !Bar.Top.Active && !Bar.Bottom.Active && Bar.Body.Contains(clickPos);
            _bodyClickPos = clickPos;
        };
        _mouseUpHandler = (s, e) => {
            Bar.Top.Active = false;
            Bar.Bottom.Active = false;

            if (_bodyClickArmed) {
                _bodyClickArmed = false;

                var pos = this.PointToClient(Cursor.Position);
                const int clickTolerance = 3;
                if (Bar.Body.Contains(pos) && Math.Abs(pos.X - _bodyClickPos.X) <= clickTolerance && Math.Abs(pos.Y - _bodyClickPos.Y) <= clickTolerance) {
                    RetargetRequested?.Invoke(this, EventArgs.Empty);
                }
            }
        };
        _dragHandler = (s, e) => {
            var pos = this.PointToClient(Cursor.Position);
            if (Bar.Top.Active) {
                var offset = Bar.Top.CursorPos;
                pos = pos - offset;

                TopOffset = LayoutStyle is LayoutStyle.Left or LayoutStyle.Right ? pos.Y : pos.X;
                Bar = GetBar(Size, TopOffset, BottomOffset);
                Bar.Top.Hover = Bar.Top.Active = true;
                Bar.Top.CursorPos = offset;
                this.Invalidate(FullBar);
            }

            if (Bar.Bottom.Active) {
                var offset = Bar.Bottom.CursorPos;
                pos = pos + offset;

                BottomOffset = LayoutStyle is LayoutStyle.Left or LayoutStyle.Right ? Height - pos.Y : Width - pos.X;
                Bar = GetBar(Size, TopOffset, BottomOffset);
                Bar.Bottom.Hover = Bar.Bottom.Active = true;
                Bar.Bottom.CursorPos = offset;
                this.Invalidate(FullBar);
            }
        };

        this.Form.Layout += _layoutHandler;
        this.Form.MouseEnter += _hoverHandler;
        this.Form.MouseHover += _hoverHandler;
        this.Form.MouseMove += _hoverMoveHandler;
        this.Form.MouseLeave += _hoverHandler;
        this.Form.MouseDown += _mouseDownHandler;
        this.Form.MouseUp += _mouseUpHandler;
        this.Form.MouseMove += _dragHandler;
    }

    // bars created after the form is already visible won't get a Layout event to
    // populate Bar until something actually resizes the panel, so compute it eagerly
    // once Bounds/LayoutStyle/offsets have been set (typically right after construction)
    public void RecalculateBar()
    {
        Bar = GetBar(Size, TopOffset, BottomOffset);
    }

    public void Dispose()
    {
        _panel.Sliders.Remove(this);
        this.Form.Layout -= _layoutHandler;
        this.Form.MouseEnter -= _hoverHandler;
        this.Form.MouseHover -= _hoverHandler;
        this.Form.MouseMove -= _hoverMoveHandler;
        this.Form.MouseLeave -= _hoverHandler;
        this.Form.MouseDown -= _mouseDownHandler;
        this.Form.MouseUp -= _mouseUpHandler;
        this.Form.MouseMove -= _dragHandler;

        if (Visible) {
            this.Invalidate(FullBar);
        }
    }

    private Point PointToClient(Point position)
    {
        position = this.Form.PointToClient(position);
        return new Point(position.X - Bounds.Location.X, position.Y - Bounds.Location.Y);
    }

    // is any bar drawn after this one in the panel's paint order (so visually on top of it) also
    // sitting on this pixel? Sliders don't know their own z-order relative to each other otherwise,
    // since they all independently react to the same shared panel mouse events.
    private bool IsCoveredAt(Point localPos)
    {
        var global = new Point(localPos.X + Bounds.X, localPos.Y + Bounds.Y);
        var index = _panel.Sliders.IndexOf(this);
        for (var i = index + 1; i < _panel.Sliders.Count; i++) {
            if (_panel.Sliders[i].HitTestGlobal(global)) {
                return true;
            }
        }

        return false;
    }

    private bool HitTestGlobal(Point globalPos)
    {
        if (!Visible || Bar == null) return false;

        var local = new Point(globalPos.X - Bounds.X, globalPos.Y - Bounds.Y);
        return Bar.Top.Contains(local)
            || Bar.Bottom.Contains(local)
            || (Bar.CloseButton != Rectangle.Empty && Bar.CloseButton.Contains(local))
            || Bar.Body.Contains(local);
    }

    private void HandleHover()
    {
        var pos = this.PointToClient(Cursor.Position);

        // another bar drawn on top of this one owns this pixel - don't show resize/close/retarget
        // affordances for ours here, or its cursor and this one's would fight over every move
        if (IsCoveredAt(pos)) {
            if (Bar.Top.Hover) { Bar.Top.Hover = false; this.Invalidate(Bar.Top); }
            if (Bar.Bottom.Hover) { Bar.Bottom.Hover = false; this.Invalidate(Bar.Bottom); }
            if (Bar.CloseHover) { Bar.CloseHover = false; this.Invalidate(Bar.CloseButton); }
            Bar.BodyHover = false;
            return;
        }

        if (Bar.Top.Contains(pos)) {
            this.Form.Cursor = LayoutStyle is LayoutStyle.Left or LayoutStyle.Right ? Cursors.SizeNS : Cursors.SizeWE;
            if (!Bar.Top.Hover) {
                Bar.Top.Hover = true;
                this.Invalidate(Bar.Top);
            }
        }
        else if (Bar.Top.Hover) {
            Bar.Top.Hover = false;
            this.Form.Cursor = Cursors.Default;
            this.Invalidate(Bar.Top);
        }

        if (Bar.Bottom.Contains(pos)) {
            this.Form.Cursor = LayoutStyle is LayoutStyle.Left or LayoutStyle.Right ? Cursors.SizeNS : Cursors.SizeWE;
            if (!Bar.Bottom.Hover) {
                Bar.Bottom.Hover = true;
                this.Invalidate(Bar.Bottom);
            }
        }
        else if (Bar.Bottom.Hover) {
            Bar.Bottom.Hover = false;
            this.Form.Cursor = Cursors.Default;
            this.Invalidate(Bar.Bottom);
        }

        if (Visible && Bar.CloseButton != Rectangle.Empty && Bar.CloseButton.Contains(pos)) {
            this.Form.Cursor = Cursors.Hand;
            if (!Bar.CloseHover) {
                Bar.CloseHover = true;
                this.Invalidate(Bar.CloseButton);
            }
        }
        else if (Bar.CloseHover) {
            Bar.CloseHover = false;
            this.Form.Cursor = Cursors.Default;
            this.Invalidate(Bar.CloseButton);
        }

        var overCloseButton = Bar.CloseButton != Rectangle.Empty && Bar.CloseButton.Contains(pos);
        if (Visible && Bar.Body.Contains(pos) && !overCloseButton) {
            this.Form.Cursor = Cursors.Hand;
            Bar.BodyHover = true;
        }
        else if (Bar.BodyHover) {
            Bar.BodyHover = false;
            this.Form.Cursor = Cursors.Default;
        }
    }

    private void Invalidate(GraphicsPath path)
    {
        var points = path.PathData.Points!;
        var types = path.PathData.Types!;
        for (int i = 0; i < points.Length; i++) {
            points[i] = new PointF(points[i].X + Bounds.Location.X, points[i].Y + Bounds.Location.Y);
        }

        path = new GraphicsPath(points, types);
        var region = new Region(path);
        this.Form.Invalidate(region);
    }

    private void Invalidate(Rectangle rect)
    {
        var path = new GraphicsPath();
        path.AddRectangle(rect);
        this.Invalidate(path);
    }

    public void OnPaintBackground(PaintEventArgs e)
    {
        //base.OnPaintBackground(e);
        if (e.Graphics.ClipBounds.IntersectsWith(Bounds)) {
            e.Graphics.TranslateTransform(Bounds.X, Bounds.Y);
            FullBar.Draw(e.Graphics, BackColor);
            e.Graphics.ResetTransform();
        }
    }

    public void OnPaint(PaintEventArgs e)
    {
        //base.OnPaint(e);
        if (Visible && e.Graphics.ClipBounds.IntersectsWith(Bounds)) {
            e.Graphics.TranslateTransform(Bounds.X, Bounds.Y);
            var vertical = LayoutStyle is LayoutStyle.Left or LayoutStyle.Right;
            Bar.Draw(e.Graphics, label: (TargetScreenId + 1).ToString(), vertical: vertical);
            e.Graphics.ResetTransform();
        }
    }


    private Bar GetBar(Size rect, int topOffset, int bottomOffset)
    {
        var size = Math.Min(rect.Width, rect.Height);

        switch (LayoutStyle) {
            case LayoutStyle.Top:
                return new Bar(
                    new Triangle(0, 0, size, 0, size, size) + new Size(topOffset, 0),
                    new Rectangle(size + topOffset, 0, rect.Width - size * 2 - topOffset - bottomOffset, rect.Height),
                    new Triangle(size, 0, 0, 0, 0, size) + new Size(rect.Width - size - bottomOffset, 0)
                );
            case LayoutStyle.Left:
                return new Bar(
                    new Triangle(0, 0, 0, size, size, size) + new Size(0, topOffset),
                    new Rectangle(0, size + topOffset, rect.Width, rect.Height - size * 2 - topOffset - bottomOffset),
                    new Triangle(0, size, 0, 0, size, 0) + new Size(0, rect.Height - size - bottomOffset)
                );
            case LayoutStyle.Right:
                return new Bar(
                    new Triangle(0, size, size, size, size, 0) + new Size(0, topOffset),
                    new Rectangle(0, size + topOffset, rect.Width, rect.Height - size * 2 - topOffset - bottomOffset),
                    new Triangle(0, 0, size, size, size, 0) + new Size(0, rect.Height - size - bottomOffset)
                );
            case LayoutStyle.Bottom:
                return new Bar(
                    new Triangle(0, size, size, size, size, 0) + new Size(topOffset, 0),
                    new Rectangle(size + topOffset, 0, rect.Width - size * 2 - topOffset - bottomOffset, rect.Height),
                    new Triangle(size, size, 0, 0, 0, size) + new Size(rect.Width - size - bottomOffset, 0)
                );
            default:
                throw new ArgumentOutOfRangeException(nameof(LayoutStyle));
        }
    }

    public void Show()
    {
        if (!Visible) {
            Visible = true;
            this.Invalidate(FullBar);
        }
    }

    public void Hide()
    {
        if (Visible) {
            Visible = false;
            this.Invalidate(FullBar);
        }
    }
}

internal class Bar {
    public Triangle Top { get; }
    public Rectangle Body { get; }
    public Triangle Bottom { get; }
    public Rectangle CloseButton { get; }
    public bool CloseHover { get; set; }
    public bool BodyHover { get; set; }

    public Bar(Triangle top, Rectangle body, Triangle bottom)
    {
        Top = top;
        Body = body;
        Bottom = bottom;
        CloseButton = ComputeCloseButton(body);
    }

    private static Rectangle ComputeCloseButton(Rectangle body)
    {
        const int closeSize = 16;
        if (body.Width < closeSize + 4 || body.Height < closeSize + 4) {
            return Rectangle.Empty;
        }

        return new Rectangle(
            body.X + body.Width / 2 - closeSize / 2,
            body.Y + body.Height / 2 - closeSize / 2,
            closeSize,
            closeSize
        );
    }

    public void Draw(Graphics g, Color? bgColor = null, string? label = null, bool vertical = false)
    {
        using (var bodyBg = new SolidBrush(bgColor ?? Color.Blue)) {
            g.FillRectangle(bodyBg, Body);

            Top.Draw(g, bgColor);
            Bottom.Draw(g, bgColor);
        }

        // only draw the remove control / target label for the actual bar, not for the transparent background pass
        if (bgColor == null && CloseButton != Rectangle.Empty) {
            var fill = CloseHover ? Color.FromArgb(230, Color.Red) : Color.FromArgb(160, Color.Black);
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(Color.White, 2)) {
                g.FillEllipse(brush, CloseButton);

                var inset = CloseButton;
                inset.Inflate(-CloseButton.Width / 4, -CloseButton.Height / 4);
                g.DrawLine(pen, inset.Left, inset.Top, inset.Right, inset.Bottom);
                g.DrawLine(pen, inset.Left, inset.Bottom, inset.Right, inset.Top);
            }
        }

        if (bgColor == null && !string.IsNullOrEmpty(label)) {
            DrawLabel(g, label, vertical);
        }
    }

    private void DrawLabel(Graphics g, string label, bool vertical)
    {
        using var font = new Font(FontFamily.GenericSansSerif, 9f, FontStyle.Bold);
        var size = g.MeasureString(label, font);

        // keep well clear of the close button in the middle of the body, so put the
        // label near the leading edge of the segment instead of dead-center
        const int margin = 6;
        const int clearance = 30; // rough half-width of the close button plus some breathing room

        var state = g.Save();
        try {
            if (vertical) {
                if (Body.Height < size.Width + margin + clearance) return;

                g.TranslateTransform(Body.X + Body.Width / 2f, Body.Y + margin);
                g.RotateTransform(90);
                DrawLabelBackground(g, new RectangleF(0, 0, size.Width, size.Height));
                g.DrawString(label, font, Brushes.White, 0, 0);
            }
            else {
                if (Body.Width < size.Width + margin + clearance) return;

                var pos = new PointF(Body.X + margin, Body.Y + Body.Height / 2f - size.Height / 2f);
                DrawLabelBackground(g, new RectangleF(pos, size));
                g.DrawString(label, font, Brushes.White, pos);
            }
        }
        finally {
            g.Restore(state);
        }
    }

    private static void DrawLabelBackground(Graphics g, RectangleF textBounds)
    {
        var bg = RectangleF.Inflate(textBounds, 3, 1);
        using var brush = new SolidBrush(Color.FromArgb(160, Color.Black));
        g.FillRectangle(brush, bg);
    }

    public GraphicsPath Path {
        get {
            var path = new GraphicsPath();
            path.AddPolygon(Top.Points);
            path.AddRectangle(Body);
            path.AddPolygon(Bottom.Points);
            return path;
        }
    }

    public Region Region => new Region(Path);

    public static implicit operator Region(Bar bar)
    {
        return bar.Region;
    }

    public static implicit operator GraphicsPath(Bar bar)
    {
        return bar.Path;
    }
}

internal class Triangle {
    public bool Hover { get; set; }

    public Triangle(int x, int y, int x1, int y1, int x2, int y2)
    {
        Points = new[] { new Point(x, y), new Point(x1, y1), new Point(x2, y2) };
    }

    public Triangle(Point p1, Point p2, Point p3)
    {
        Points = new[] { p1, p2, p3 };
    }

    public Point[] Points { get; }

    public bool Contains(Point p)
    {
        var p0 = Points[0];
        var p1 = Points[1];
        var p2 = Points[2];

        var s = p0.Y * p2.X - p0.X * p2.Y + (p2.Y - p0.Y) * p.X + (p0.X - p2.X) * p.Y;
        var t = p0.X * p1.Y - p0.Y * p1.X + (p0.Y - p1.Y) * p.X + (p1.X - p0.X) * p.Y;

        if ((s < 0) != (t < 0))
            return false;

        var a = -p1.Y * p2.X + p0.Y * (p2.X - p1.X) + p0.X * (p1.Y - p2.Y) + p1.X * p2.Y;

        return a < 0 ? (s <= 0 && s + t >= a) : (s >= 0 && s + t <= a);
    }

    public GraphicsPath Path {
        get {
            var path = new GraphicsPath();
            path.AddPolygon(Points);
            return path;
        }
    }

    public Region Region => new Region(Path);
    public bool Active { get; set; }
    public Size CursorPos { get; set; }

    public static Triangle operator +(Triangle t, Size offset)
    {
        return new Triangle(
            t.Points[0] + offset,
            t.Points[1] + offset,
            t.Points[2] + offset
        );
    }

    public static implicit operator Region(Triangle triangle)
    {
        return triangle.Region;
    }

    public static implicit operator GraphicsPath(Triangle triangle)
    {
        return triangle.Path;
    }

    public void Draw(Graphics g, Color? bgColor)
    {
        var color = bgColor ?? Color.Red;
        if (bgColor == null) {
            if (Hover) {
                color = Color.FromArgb(128, color);
            }
        }

        using (var pan = new Pen(Color.FromArgb(100, Color.Black), 3))
        using (var bg = new SolidBrush(color)) {
            g.FillPolygon(bg, Points);

            if (bgColor == null) {
                g.DrawPolygon(pan, Scale(0.9f));
            }
        }
    }

    private PointF[] Scale(float s)
    {
        var A = Points[0];
        var B = Points[1];
        var C = Points[2];

        var a = Distance(B, C);
        var b = Distance(A, C);
        var c = Distance(A, B);

        float Distance(Point p1, Point p2) => (float) Math.Sqrt(Math.Abs(Math.Pow(p1.X - p2.X, 2)) + Math.Abs(Math.Pow(p1.Y - p2.Y, 2)));

        var p = a + b + c;

        var centerX = (a * A.X + b * B.X + c * C.X) / p;
        var centerY = (a * A.Y + b * B.Y + c * C.Y) / p;

        float MapX(int x) => (x - centerX) * s + centerX;
        float MapY(int y) => (y - centerY) * s + centerY;

        return new[] {
            new PointF(MapX(Points[0].X), MapY(Points[0].Y)),
            new PointF(MapX(Points[1].X), MapY(Points[1].Y)),
            new PointF(MapX(Points[2].X), MapY(Points[2].Y)),
        };
    }

    public Rectangle GetBounds()
    {
        var x = Points.Min(_ => _.X);
        var y = Points.Min(_ => _.Y);
        return new Rectangle(
            x,
            y,
            Points.Max(_ => _.X) - x,
            Points.Max(_ => _.Y) - y
        );
    }
}

public enum LayoutStyle {
    Top,
    Left,
    Right,
    Bottom
}
