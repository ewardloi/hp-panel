using HpPanel.Core;

namespace HpPanel.Controls;

internal abstract class FlatControl : Control
{
    protected FlatControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        TabStop = false;
    }

    protected float UIScale => DeviceDpi / 96f;

    protected abstract void Draw(PaintEventArgs e);

    protected sealed override void OnPaint(PaintEventArgs e)
    {
        try
        {
            Draw(e);
        }
        catch (Exception ex)
        {
            CrashLog.Write(ex, GetType().Name + " paint");
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        if (Parent is ScrollableControl sc && !sc.ContainsFocus)
        {
            sc.Focus();
        }

        base.OnMouseEnter(e);
    }
}
