using Godot;
using Mortz.Core.Text;

namespace Mortz.Client.Announcements;

[GlobalClass]
public partial class AnnouncementLine : VBoxContainer
{
    [Export] private AnnouncementTitle _title = null!;
    [Export] private RichTextLabel _detail = null!;
    [Export] private OwnedAnnouncementTitle _owned = null!;

    public void Present(string title, RichText detail, StringName themeVariation, RichText? victim = null)
    {
        _detail.Text = detail.Wrap(new Style().Center());
        _title.Visible = victim == null;
        _owned.Visible = victim != null;
        if (victim != null)
            _owned.Present(victim);
        else
            _title.Present(title, themeVariation);
    }
}
