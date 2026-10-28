using HpPanel.Core;

namespace HpPanel.Controls;

internal sealed class SectionLabel : FlatControl
{
    private readonly Font _font = Theme.MakeFont(8.5f, FontStyle.Bold);

    public SectionLabel(string text) { Text = text; }

    protected override void Dispose(bool disposing) { if (disposing) { _font.Dispose(); } base.Dispose(disposing); }

    protected override void Draw(PaintEventArgs e)
    {
        var p = Theme.Current;
        UI.Prepare(e.Graphics, this, p);
        UI.Text(e.Graphics, Text.ToUpperInvariant(), _font, p.Muted, new RectangleF(4 * UIScale, 0, Width, Height));
    }
}
