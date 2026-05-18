using System.Drawing.Text;

namespace Rex.Controls;

internal sealed class ResourceListPanel : Control
{
    public sealed class RowItem
    {
        public required string  Name         { get; set; }
        public required string  TypeLabel    { get; set; }
        public required string  DisplayValue { get; set; }
        public          string? Comment      { get; set; }
        public          Image?  Preview      { get; set; }
    }

    public sealed class RowActionEventArgs(int index, bool isPreviewZone) : EventArgs
    {
        public int  Index         { get; } = index;
        public bool IsPreviewZone { get; } = isPreviewZone;
    }

    public event EventHandler<RowActionEventArgs>? RowDoubleClick;
    public event EventHandler?                     SelectionChanged;
    public event EventHandler?                     DeleteRequested;
    public event EventHandler?                     RenameRequested;

    private const int RowHeight    = 80;
    private const int PreviewWidth = 88;
    private const int TextPad      = 12;

    private readonly List<RowItem>  _items    = [];
    private readonly SortedSet<int> _selected = [];

    private int _hot    = -1;
    private int _anchor = -1;

    private readonly VScrollBar _vscroll = new() { SmallChange = RowHeight };

    private int ScrollY    => _vscroll.Value;
    private int ClientW    => Width - (_vscroll.Visible ? _vscroll.Width : 0);
    private int MaxScrollY => Math.Max(0, _items.Count * RowHeight - Height);

    private Font _nameFont    = null!;
    private Font _valueFont   = null!;
    private Font _commentFont = null!;

    public IReadOnlyList<RowItem>   Items           => _items;
    public IReadOnlyCollection<int> SelectedIndices => _selected;
    public int                      SelectedIndex   => _selected.Count == 1 ? _selected.Min : -1;

    public void AddItem(RowItem item)
    {
        _items.Add(item);
        SyncScrollBar();
        Invalidate();
    }

    public void UpdateItem(int index, RowItem item)
    {
        _items[index] = item;
        InvalidateRow(index);
    }

    public void RemoveAt(int index)
    {
        _items.RemoveAt(index);

        var newSel = new SortedSet<int>(
            _selected
                .Where(i => i != index)
                .Select(i => i > index ? i - 1 : i));

        _selected.Clear();
        foreach (int i in newSel) _selected.Add(i);

        if (_anchor > index) _anchor--;
        else if (_anchor == index) _anchor = -1;

        SyncScrollBar();
        Invalidate();
    }

    public void Clear()
    {
        _items.Clear();
        _selected.Clear();
        _hot    = -1;
        _anchor = -1;
        _vscroll.Value = 0;
        SyncScrollBar();
        Invalidate();
    }

    public ResourceListPanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint  |
            ControlStyles.UserPaint             |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        TabStop   = true;
        BackColor = SystemColors.ControlDark;

        _vscroll.ValueChanged += (_, _) => Invalidate();
        Controls.Add(_vscroll);

        RebuildFonts();
        PositionScrollBar();
    }

    private void RebuildFonts()
    {
        _nameFont?.Dispose();
        _valueFont?.Dispose();
        _commentFont?.Dispose();

        _nameFont    = new Font(Font.FontFamily, 10f, FontStyle.Bold);
        _valueFont   = new Font(Font.FontFamily, 9f, FontStyle.Regular);
        _commentFont = new Font(Font.FontFamily, 8.5f, FontStyle.Italic);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);

        RebuildFonts();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _nameFont.Dispose();
            _valueFont.Dispose();
            _commentFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void PositionScrollBar() =>
        _vscroll.SetBounds(
            Width - SystemInformation.VerticalScrollBarWidth,
            0,
            SystemInformation.VerticalScrollBarWidth,
            Height);

    private void SyncScrollBar()
    {
        int total = _items.Count * RowHeight;
        bool needed = total > Height;

        _vscroll.Visible = needed;

        if (needed)
        {
            _vscroll.LargeChange = Height;
            _vscroll.Maximum     = total - Height + _vscroll.LargeChange - 1;
            _vscroll.Value       = Math.Min(_vscroll.Value, MaxScrollY);
        }
        else
        {
            _vscroll.Value = 0;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        PositionScrollBar();
        SyncScrollBar();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_vscroll.Visible)
            _vscroll.Value = Math.Clamp(
                ScrollY - e.Delta / 120 * RowHeight,
                0,
                MaxScrollY);

        base.OnMouseWheel(e);
    }

    private int RowAt(int screenY)
    {
        int row = (screenY + ScrollY) / RowHeight;
        return (row >= 0 && row < _items.Count) ? row : -1;
    }

    private static bool IsPreviewZone(int x) => x < PreviewWidth;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int row = RowAt(e.Y);

        if (row != _hot)
        {
            InvalidateRow(_hot);
            _hot = row;
            InvalidateRow(_hot);
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        InvalidateRow(_hot);
        _hot = -1;

        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();

        int row = RowAt(e.Y);
        if (row < 0)
        {
            base.OnMouseDown(e);
            return;
        }

        bool ctrl  = (ModifierKeys & Keys.Control) != 0;
        bool shift = (ModifierKeys & Keys.Shift)   != 0;

        if (shift && _anchor >= 0)
        {
            _selected.Clear();

            for (int i = Math.Min(_anchor, row); i <= Math.Max(_anchor, row); i++)
                _selected.Add(i);
        }
        else if (ctrl)
        {
            if (!_selected.Remove(row))
                _selected.Add(row);

            _anchor = row;
        }
        else
        {
            _selected.Clear();
            _selected.Add(row);
            _anchor = row;
        }

        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);

        base.OnMouseDown(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        int row = RowAt(e.Y);

        if (row >= 0)
            RowDoubleClick?.Invoke(this, new RowActionEventArgs(row, IsPreviewZone(e.X)));

        base.OnMouseDoubleClick(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down or Keys.Delete or Keys.F2
        || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Up:
                MoveSelection(-1, e.Shift);
                e.Handled = true;
                break;
            case Keys.Down:
                MoveSelection(+1, e.Shift);
                e.Handled = true;
                break;
            case Keys.A when e.Control:
                _selected.Clear();

                for (int i = 0; i < _items.Count; i++)
                    _selected.Add(i);

                Invalidate();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Keys.Delete:
                DeleteRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
            case Keys.F2:
                RenameRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }

        base.OnKeyDown(e);
    }

    private void MoveSelection(int delta, bool extend)
    {
        if (_items.Count == 0)
            return;

        int next = Math.Clamp(
            (_anchor < 0 ? 0 : _anchor) + delta,
            0,
            _items.Count - 1);

        if (extend && _anchor >= 0)
        {
            if (!_selected.Remove(next))
                _selected.Add(next);
        }
        else
        {
            _selected.Clear();
            _selected.Add(next);
        }

        _anchor = next;

        EnsureVisible(next);

        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureVisible(int index)
    {
        int top    = index * RowHeight;
        int bottom = top + RowHeight;

        if (top < ScrollY)
            _vscroll.Value = top;
        else if (bottom > ScrollY + Height)
            _vscroll.Value = Math.Min(bottom - Height, MaxScrollY);
    }

    private void InvalidateRow(int index)
    {
        if (index < 0 || index >= _items.Count)
            return;

        Invalidate(new Rectangle(
            0,
            index * RowHeight - ScrollY,
            ClientW,
            RowHeight));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        int first = Math.Max(0, ScrollY / RowHeight);
        int last  = Math.Min(_items.Count - 1, (ScrollY + Height) / RowHeight + 1);

        for (int i = first; i <= last; i++)
        {
            PaintRow(
                g,
                i,
                new Rectangle(
                    0,
                    i * RowHeight - ScrollY,
                    ClientW,
                    RowHeight));
        }

        int contentBottom = _items.Count * RowHeight - ScrollY;

        if (contentBottom < Height)
        {
            using var br = new SolidBrush(BackColor);
            g.FillRectangle(br, 0, contentBottom, ClientW, Height - contentBottom);
        }

        if (Focused && _anchor >= 0 && _anchor < _items.Count)
        {
            int y = _anchor * RowHeight - ScrollY;

            ControlPaint.DrawFocusRectangle(
                g,
                new Rectangle(1, y + 1, ClientW - 2, RowHeight - 2));
        }
    }

    private static readonly Color AccentBlue   = SystemColors.Highlight;
    private static readonly Color HoverBg      = SystemColors.Info;
    private static readonly Color AltRowBg     = SystemColors.ControlDarkDark;
    private static readonly Color SeparatorCol = SystemColors.Desktop;
    private static readonly Color PreviewBg    = SystemColors.Menu;
    private static readonly Color NameColor    = SystemColors.ControlText;
    private static readonly Color ValueColor   = SystemColors.ControlText;
    private static readonly Color CommentColor = SystemColors.GrayText;

    private void PaintRow(Graphics g, int index, Rectangle bounds)
    {
        var item = _items[index];

        bool sel = _selected.Contains(index);
        bool hot = index == _hot && !sel;

        var bg = sel
            ? AccentBlue
            : hot
                ? HoverBg
                : (index & 1) == 1
                    ? AltRowBg
                    : BackColor;

        using (var bgBrush = new SolidBrush(bg))
            g.FillRectangle(bgBrush, bounds);

        if (!sel)
        {
            using var sep = new Pen(SeparatorCol);

            g.DrawLine(
                sep,
                bounds.Left,
                bounds.Bottom - 1,
                bounds.Right - 1,
                bounds.Bottom - 1);
        }

        PaintPreviewColumn(g, item, bounds, sel);
        PaintTextColumn(g, item, bounds, sel);
    }

    private void PaintPreviewColumn(Graphics g, RowItem item, Rectangle rowBounds, bool selected)
    {
        var zone = new Rectangle(
            rowBounds.Left,
            rowBounds.Top,
            PreviewWidth,
            rowBounds.Height);

        using (var pb = new SolidBrush(
                   selected
                       ? Color.FromArgb(25, 0, 0, 0)
                       : PreviewBg))
        {
            g.FillRectangle(pb, zone);
        }

        if (item.Preview is not null)
        {
            const int maxThumb = 56;

            var imgSize = item.Preview.Size;

            float ratioX = (float)maxThumb / imgSize.Width;
            float ratioY = (float)maxThumb / imgSize.Height;
            float ratio  = Math.Min(ratioX, ratioY);

            int drawW = Math.Max(1, (int)Math.Round(imgSize.Width  * ratio));
            int drawH = Math.Max(1, (int)Math.Round(imgSize.Height * ratio));

            int px = zone.Left + (zone.Width  - drawW) / 2;
            int py = zone.Top  + (zone.Height - drawH) / 2;

            var oldInterpolation = g.InterpolationMode;
            var oldPixelOffset   = g.PixelOffsetMode;

            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode   = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            g.DrawImage(
                item.Preview,
                new Rectangle(px, py, drawW, drawH),
                new Rectangle(0, 0, imgSize.Width, imgSize.Height),
                GraphicsUnit.Pixel);

            g.InterpolationMode = oldInterpolation;
            g.PixelOffsetMode   = oldPixelOffset;
        }
        else
        {
            var letter = item.TypeLabel.Length > 0
                ? item.TypeLabel[..1]
                : "?";

            var letterColor = selected
                ? Color.FromArgb(200, 255, 255, 255)
                : ValueColor;

            using var lf = new Font(Font.FontFamily, 18f, FontStyle.Bold);
            using var lb = new SolidBrush(letterColor);

            var sz = g.MeasureString(letter, lf);

            g.DrawString(
                letter,
                lf,
                lb,
                zone.Left + (PreviewWidth - sz.Width) / 2f,
                zone.Top  + (RowHeight - sz.Height) / 2f);
        }

        using var div = new Pen(
            selected
                ? Color.FromArgb(50, 255, 255, 255)
                : SeparatorCol);

        g.DrawLine(
            div,
            PreviewWidth,
            rowBounds.Top,
            PreviewWidth,
            rowBounds.Bottom - 1);
    }

    private void PaintTextColumn(Graphics g, RowItem item, Rectangle rowBounds, bool selected)
    {
        var textPrimary   = selected ? Color.White : NameColor;
        var textSecondary = selected ? Color.FromArgb(220, 255, 255, 255) : ValueColor;
        var textMuted     = selected ? Color.FromArgb(170, 255, 255, 255) : CommentColor;

        int tx = PreviewWidth + TextPad;
        int tw = rowBounds.Right - tx - TextPad;

        using (var nb = new SolidBrush(textPrimary))
        {
            g.DrawString(
                item.Name,
                _nameFont,
                nb,
                new RectangleF(tx, rowBounds.Top + 12f, tw, 20f),
                Clip1Line);
        }

        using (var vb = new SolidBrush(textSecondary))
        {
            g.DrawString(
                item.DisplayValue,
                _valueFont,
                vb,
                new RectangleF(tx, rowBounds.Top + 35f, tw, 18f),
                Clip1Line);
        }

        if (!string.IsNullOrEmpty(item.Comment))
        {
            using var cb = new SolidBrush(textMuted);

            g.DrawString(
                item.Comment,
                _commentFont,
                cb,
                new RectangleF(tx, rowBounds.Top + 55f, tw, 16f),
                Clip1Line);
        }
    }

    private static readonly StringFormat Clip1Line = new()
    {
        Trimming    = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.NoWrap
    };
}
