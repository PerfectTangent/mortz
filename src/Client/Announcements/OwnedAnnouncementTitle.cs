using Godot;
using Mortz.Core.Text;

namespace Mortz.Client.Announcements;

[GlobalClass]
public partial class OwnedAnnouncementTitle : Control
{
    [Export] private RichTextLabel _victim = null!;
    [Export] private ColorRect _slash = null!;
    [Export] private Label _ink = null!;

    private bool _fitting;

    public override void _Ready()
    {
        _slash.Material = (Material)_slash.Material.Duplicate();
        Resized += Fit;
        ThemeChanged += Fit;
        _victim.Resized += Fit;
        Fit();
    }

    public void Present(RichText victim)
    {
        _victim.Text = victim.Wrap(new Style().Center());
        Fit();
        var material = (ShaderMaterial)_slash.Material;
        material.SetShaderParameter("seed", GD.Randf() * 100f);
        material.SetShaderParameter("progress", 0f);
        _ink.Modulate = new Color(1f, 1f, 1f, 0f);
        _ink.Scale = new Vector2(1.3f, 1.45f);

        float slashSeconds = Math.Max(1, _ink.GetThemeConstant("slash_duration")) / 1000f;
        float impactSeconds = Math.Max(1, _ink.GetThemeConstant("impact_duration")) / 1000f;
        Tween tween = CreateTween();
        tween.TweenMethod(Callable.From<float>(value => material.SetShaderParameter("progress", value)),
            0f, 1f, slashSeconds).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(_ink, "modulate:a", 1f, 0.035f);
        tween.Parallel().TweenProperty(_ink, "scale", Vector2.One, impactSeconds)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    private void Fit()
    {
        if (!IsNodeReady() || _fitting)
            return;
        _fitting = true;
        try
        {
            _victim.Size = new Vector2(Size.X, Math.Max(1, _victim.GetContentHeight()));
            float victimHeight = _victim.GetContentHeight();
            float slashWidth = Math.Min(Math.Max(1f, Size.X - 8f), _victim.GetContentWidth() + 20f);
            _slash.Size = new Vector2(slashWidth, Math.Max(12f, victimHeight));
            _slash.Position = new Vector2((Size.X - slashWidth) / 2f, 0f);
            _slash.Color = _ink.GetThemeColor("slash_color");
            ((ShaderMaterial)_slash.Material).SetShaderParameter("area_size", _slash.Size);

            float available = Math.Max(1f, Size.X - _ink.GetThemeConstant("text_padding"));
            int fontSize = AnnouncementFont.Fit(_ink, available);
            Font font = _ink.GetThemeFont("font");
            _ink.Size = new Vector2(Size.X, font.GetHeight(fontSize));
            _ink.Position = new Vector2(0f, victimHeight - _ink.GetThemeConstant("victim_overlap"));
            _ink.PivotOffset = _ink.Size / 2f;
            _ink.Rotation = Mathf.DegToRad(_ink.GetThemeConstant("impact_angle"));
            CustomMinimumSize = new Vector2(0f, Math.Max(44f, _ink.Position.Y + _ink.Size.Y - 4f));
        }
        finally
        {
            _fitting = false;
        }
    }
}
