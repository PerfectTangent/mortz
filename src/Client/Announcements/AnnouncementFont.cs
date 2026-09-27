using Godot;

namespace Mortz.Client.Announcements;

public static class AnnouncementFont
{
    public static int Fit(Label label, float availableWidth)
    {
        // Discard the previous fit so a wider viewport restores the theme's size.
        label.RemoveThemeFontSizeOverride("font_size");
        int preferredSize = label.GetThemeFontSize("font_size");
        int fontSize = preferredSize;
        Font font = label.GetThemeFont("font");
        int minimumSize = Math.Max(1, label.GetThemeConstant("minimum_font_size"));
        while (fontSize > minimumSize && font.GetStringSize(label.Text, fontSize: fontSize).X > availableWidth)
        {
            fontSize--;
        }
        if (fontSize != preferredSize)
            label.AddThemeFontSizeOverride("font_size", fontSize);
        return fontSize;
    }
}
