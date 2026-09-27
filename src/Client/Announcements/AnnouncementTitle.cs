using Godot;

namespace Mortz.Client.Announcements;

[GlobalClass]
public partial class AnnouncementTitle : Control
{
    private const float MAX_TENSION_STRETCH = 1.42f;

    [Export] private Label _ink = null!;
    [Export] private Label _overprint = null!;
    [Export] private ColorRect _smear = null!;
    [Export] private Control _tension = null!;
    [Export] private TextureRect _left = null!;
    [Export] private TextureRect _right = null!;
    [Export] private Line2D _sinew = null!;
    [Export] private Line2D _highlight = null!;
    [Export] private Line2D _loose = null!;
    [Export] private Line2D _rightSinew = null!;
    [Export] private Line2D _rightHighlight = null!;
    [Export] private Line2D _rightLoose = null!;

    private bool _underTension;
    private float _pull = 1f;
    private float _textWidth;
    private float _bloodBurst;
    private float _bloodStain;
    private float _bloodSeverity;
    private float _bloodProgress = 1f;
    private float Severity { get; set; }
    private float TensionStretch => Mathf.Lerp(1.02f, MAX_TENSION_STRETCH, Severity);
    private bool _refreshingStyle;

    public override void _Ready()
    {
        _ink.Material = (Material)_ink.Material.Duplicate();
        _overprint.Material = (Material)_overprint.Material.Duplicate();
        _smear.Material = (Material)_smear.Material.Duplicate();
        Resized += RefreshStyle;
        ThemeChanged += RefreshStyle;
        RefreshStyle();
    }

    public void Present(string title, StringName themeVariation)
    {
        _ink.Text = _overprint.Text = title;
        _ink.ThemeTypeVariation = themeVariation;
        RefreshStyle();
        float seed = GD.Randf() * 100f;
        ((ShaderMaterial)_ink.Material).SetShaderParameter("seed", seed);
        ((ShaderMaterial)_overprint.Material).SetShaderParameter("seed", seed + 7f);
        ((ShaderMaterial)_smear.Material).SetShaderParameter("seed", seed);

        if (_bloodBurst > 0f)
        {
            SetBloodProgress(0f);
            CreateTween().TweenMethod(Callable.From<float>(SetBloodProgress), 0f, 1f, 0.42f)
                .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        }

        Tween impact = CreateTween();
        if (_underTension)
        {
            _pull = 0f;
            FitTension();
            impact.TweenMethod(Callable.From<float>(value => { _pull = value; FitTension(); }),
                    0f, 1f, 0.68f - Severity * 0.13f)
                .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
        }
        else
        {
            Scale = new Vector2(1.035f + Severity * 0.16f, 1.04f + Severity * 0.22f);
            Modulate = new Color(1f, 1f, 1f, 0f);
            impact.SetParallel();
            impact.TweenProperty(this, "modulate:a", 1f, 0.09f - Severity * 0.055f);
            impact.TweenProperty(this, "scale", Vector2.One, 0.22f - Severity * 0.11f)
                .SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
            if (Severity >= 0.75f)
            {
                impact.SetParallel(false);
                impact.TweenProperty(this, "rotation", -0.012f * Severity, 0.035f);
                impact.TweenProperty(this, "rotation", 0f, 0.1f);
            }
        }
    }

    private void SetBloodProgress(float progress)
    {
        _bloodProgress = progress;
        ((ShaderMaterial)_smear.Material).SetShaderParameter("burst_progress", progress);
        ((ShaderMaterial)_ink.Material).SetShaderParameter("blood_stain", Math.Max(_bloodStain, _bloodBurst * progress));
    }

    private void RefreshStyle()
    {
        if (!IsNodeReady() || _refreshingStyle)
            return;
        _refreshingStyle = true;
        try
        {
            _underTension = _ink.GetThemeConstant("tension") != 0;
            Severity = Mathf.Clamp(_ink.GetThemeConstant("severity") / 100f, 0f, 1f);
            float bloodFloor = Mathf.Clamp(_ink.GetThemeConstant("blood_floor") / 100f, 0f, 1f);
            _bloodSeverity = Mathf.Lerp(bloodFloor, 1f, Severity);
            _tension.Visible = _underTension;
            _overprint.Visible = _underTension ? Severity >= 0.75f : Severity >= 0.2f;
            _loose.Visible = _rightLoose.Visible = Severity >= 0.4f;
            _highlight.Visible = _rightHighlight.Visible = Severity >= 0.2f;
            _sinew.Width = _rightSinew.Width = _ink.GetThemeConstant("sinew_width");
            _loose.Width = _rightLoose.Width = _ink.GetThemeConstant("loose_width");
            _sinew.DefaultColor = _rightSinew.DefaultColor = _ink.GetThemeColor("sinew_color");
            _highlight.DefaultColor = _rightHighlight.DefaultColor = _ink.GetThemeColor("sinew_highlight_color");
            _loose.DefaultColor = _rightLoose.DefaultColor = _ink.GetThemeColor("loose_color");
            var blood = (ShaderMaterial)_smear.Material;
            blood.SetShaderParameter("blood_dark", _ink.GetThemeColor("blood_dark"));
            blood.SetShaderParameter("blood_light", _ink.GetThemeColor("blood_light"));
            blood.SetShaderParameter("severity", _bloodSeverity);
            _bloodStain = Mathf.Clamp(_ink.GetThemeConstant("blood_stain") / 100f, 0f, 1f);
            _bloodBurst = Mathf.Clamp(_ink.GetThemeConstant("blood_burst") / 100f, 0f, 1f);
            blood.SetShaderParameter("blood_burst", _bloodBurst);
            ((ShaderMaterial)_ink.Material).SetShaderParameter("blood_color", _ink.GetThemeColor("blood_ink"));
            SetBloodProgress(_bloodProgress);
            Fit();
        }
        finally
        {
            _refreshingStyle = false;
        }
    }

    private void Fit()
    {
        if (!IsNodeReady())
            return;
        PivotOffset = Size / 2f;
        Font font = _ink.GetThemeFont("font");
        float padding = _ink.GetThemeConstant("text_padding");
        if (_underTension)
            padding *= Math.Min(1f, Size.X / 400f);
        float available = Math.Max(1f, Size.X - padding);
        // A shared fit budget keeps stronger stretch from being cancelled by a smaller font.
        if (_underTension)
            available /= MAX_TENSION_STRETCH;
        int fontSize = AnnouncementFont.Fit(_ink, available);
        _textWidth = font.GetStringSize(_ink.Text, fontSize: fontSize).X;
        _overprint.AddThemeFontOverride("font", font);
        _overprint.AddThemeFontSizeOverride("font_size", fontSize);
        float textHeight = Math.Max(_ink.GetThemeConstant("text_height"), font.GetHeight(fontSize));
        CustomMinimumSize = new Vector2(0f, Math.Max(_ink.GetThemeConstant("title_height"), textHeight - 4f));
        foreach (Label label in new[] { _ink, _overprint })
        {
            label.Size = new Vector2(_textWidth + 6f, textHeight);
            label.Position = new Vector2((Size.X - label.Size.X) / 2f, -1f);
            label.PivotOffset = label.Size / 2f;
            label.Rotation = _underTension ? 0f : -0.012f - Severity * 0.025f;
        }
        _overprint.Position += new Vector2(1f + Severity * 2f, 1f + Severity);
        float smearWidth = _textWidth * (0.72f + _bloodSeverity * 0.28f) + _bloodSeverity * 24f;
        _smear.Position = new Vector2((Size.X - smearWidth) / 2f, 29f - _bloodSeverity * 18f);
        _smear.Size = new Vector2(smearWidth, 10f + _bloodSeverity * 22f);
        if (_bloodBurst > 0f)
        {
            float width = Math.Min(Size.X - 8f, _textWidth + 56f);
            _smear.Position = new Vector2((Size.X - width) / 2f, 0f);
            _smear.Size = new Vector2(width, CustomMinimumSize.Y);
        }
        ((ShaderMaterial)_smear.Material).SetShaderParameter("area_size", _smear.Size);
        FitTension();
    }

    private void FitTension()
    {
        if (!_underTension)
        {
            _ink.Scale = _overprint.Scale = Vector2.One;
            return;
        }

        float tighten = Mathf.Clamp(_pull / 0.42f, 0f, 1f);
        float strain = Mathf.Clamp((_pull - 0.04f) / 0.96f, 0f, 1f);
        float relaxation = Mathf.SmoothStep(0.8f, 1f, strain);
        float restingSag = relaxation * (1f - Severity) * (1f - Severity) * 5f;
        float stretch = Mathf.Lerp(1f, TensionStretch, strain);
        _ink.Scale = _overprint.Scale = new Vector2(stretch, Mathf.Lerp(1f, 0.99f - Severity * 0.19f, strain));
        float center = Size.X / 2f;
        float halfText = _textWidth / 2f;
        float textEdge = halfText * stretch;
        float ornamentScale = Math.Min(1f, Size.X / 400f);
        float hardwareScale = (0.7f + Severity * 0.3f) * ornamentScale;
        // Reserve room for both cords and fasteners.
        float cordLength = Math.Min((24f + Severity * 14f) * ornamentScale,
            Math.Max(4f, center - halfText * TensionStretch - 36f * hardwareScale));
        // The fasteners lead the stretch while both motions overlap.
        float reach = Mathf.Lerp(halfText + 8f * ornamentScale,
            halfText * TensionStretch + cordLength, tighten);
        float midline = _ink.Position.Y + _ink.Size.Y / 2f;
        _sinew.Width = _rightSinew.Width = Math.Max(1.2f,
            _ink.GetThemeConstant("sinew_width") * Mathf.Lerp(1f, 0.72f, strain));
        _left.Scale = _right.Scale = Vector2.One * hardwareScale;
        _left.Position = new Vector2(center - reach - 32f * hardwareScale, midline - 12f * hardwareScale);
        _right.Position = new Vector2(center + reach - 4f * hardwareScale, midline - 12f * hardwareScale);
        FitCord(new Vector2(center - textEdge, midline), new Vector2(center - reach, midline),
            _sinew, _highlight, _loose, tighten, restingSag);
        FitCord(new Vector2(center + textEdge, midline), new Vector2(center + reach, midline),
            _rightSinew, _rightHighlight, _rightLoose, tighten, restingSag);
    }

    private void FitCord(Vector2 start, Vector2 end, Line2D sinew, Line2D highlight, Line2D looseStrand,
        float tighten, float restingSag)
    {
        Vector2[] points = new Vector2[17];
        Vector2[] highlights = new Vector2[17];
        Vector2[] loose = new Vector2[17];
        for (int i = 0; i < points.Length; i++)
        {
            float t = i / 16f;
            float arch = Mathf.Sin(t * Mathf.Pi);
            float slack = 1f - tighten;
            float sag = (slack * slack * 10f + restingSag) * arch;
            points[i] = start.Lerp(end, t) + new Vector2(0, sag);
            highlights[i] = points[i] + new Vector2(0, -0.6f * arch);
            loose[i] = points[i] + new Vector2(0, -(slack * 4f + 0.35f) * arch);
        }
        sinew.Points = points;
        highlight.Points = highlights;
        looseStrand.Points = loose;
    }
}
