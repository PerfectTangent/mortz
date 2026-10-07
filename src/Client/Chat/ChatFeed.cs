using Godot;
using Mortz.Core.Chat;
using Mortz.Core.Text;
using Mortz.Extensions;
using Mortz.Protocol.Net;

namespace Mortz.Client.Chat;

/// <summary>Renders one <see cref="ClientChat"/>'s entries as child controls,
/// newest last.</summary>
public partial class ChatFeed : VBoxContainer
{
    private ClientChat _chat = null!;

    /// <summary>Raised for the line a live entry appends, not during rebuilds.</summary>
    public event Action<Control>? LineAdded;

    public event Action? Rebuilt;

    public void Bind(ClientChat chat)
    {
        _chat = chat;
        this.Subscribe(() => chat.LineAdded += OnEntryAdded, () => chat.LineAdded -= OnEntryAdded);
        this.Subscribe(() => chat.Cleared += OnCleared, () => chat.Cleared -= OnCleared);
        Rebuild();
    }

    // Not folded into the ?. call: a null-conditional skips argument
    // evaluation, and the line must exist even with no listeners.
    private void OnEntryAdded(ChatLine entry)
    {
        Control line = AddLine(entry, animate: true);
        LineAdded?.Invoke(line);
    }

    private void OnCleared() => ClearLines();

    private void Rebuild()
    {
        ClearLines();
        foreach (ChatLine entry in _chat.Lines)
        {
            AddLine(entry, animate: false);
        }
        Rebuilt?.Invoke();
    }

    private Control AddLine(ChatLine entry, bool animate)
    {
        Control line = entry is ChatLine.Roll roll
            ? RollLine.Create(roll.SenderName, roll.Value, animate)
            : BuildTextLine(entry);
        AddChild(line);
        while (GetChildCount() > NetConfig.MAX_CHAT_HISTORY)
        {
            GetChild(0).Free();
        }
        return line;
    }

    // Lines never take the mouse; the in-game overlay stays click-through.
    private static RichTextLabel BuildTextLine(ChatLine entry)
    {
        RichText content = entry.Render();
        RichText text = entry switch
        {
            ChatLine.Player player => new RichText()
                .Add(player.SenderName, new Style().Bold())
                .Add(": ").Add(content),
            ChatLine.Private => new RichText().Add("* ", new Style().Italic()).Add(content),
            _ => content,
        };
        return new RichTextLabel
        {
            Text = text.ToString(),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            BbcodeEnabled = true,
            FitContent = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
    }

    private void ClearLines()
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.Free();
        }
    }
}
