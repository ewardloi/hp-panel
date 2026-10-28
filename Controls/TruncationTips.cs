using HpPanel.Core;

namespace HpPanel.Controls;

internal sealed class TruncationTips : IDisposable
{
    private readonly ToolTip _tip = new() { UseFading = false, UseAnimation = false };
    private readonly List<(RectangleF Rect, string Text)> _cut = [];
    private int _shown = -1;

    public void Begin() => _cut.Clear();

    public void Add(Graphics g, string text, Font font, RectangleF rect)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        using var fmt = new StringFormat(StringFormatFlags.NoWrap);
        float width = g.MeasureString(text, font, new SizeF(100000f, rect.Height), fmt).Width;
        if (width > rect.Width - 1)
        {
            _cut.Add((rect, text));
        }
    }

    public void Move(Control owner, Point p)
    {
        int hit = _cut.FindIndex(x => x.Rect.Contains(p));
        if (hit == _shown)
        {
            return;
        }

        _shown = hit;
        try
        {
            if (hit < 0)
            {
                _tip.Hide(owner);
            }
            else
            {
                _tip.Show(_cut[hit].Text, owner, p.X + 12, p.Y + 22);
            }
        }
        catch (Exception e)
        {
            CrashLog.Write(e, "tooltip");
        }
    }

    public void Leave(Control owner)
    {
        _shown = -1;
        try { _tip.Hide(owner); } catch { }
    }

    public void Dispose() => _tip.Dispose();
}
