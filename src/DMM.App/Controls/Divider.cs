// Thin horizontal rule between sections of a stacked panel
namespace DMM.App;

internal sealed class Divider : Control
{
    public Divider() => BackColor = Theme.Border;

    public override Size GetPreferredSize(Size proposedSize) => new(proposedSize.Width, 1);
}
