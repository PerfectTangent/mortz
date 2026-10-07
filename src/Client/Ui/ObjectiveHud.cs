using Godot;
using Mortz.Core.Match.Objectives;

namespace Mortz.Client.Ui;

/// <summary>Shows one objective's state and hides itself for any other objective.</summary>
[GlobalClass]
public abstract partial class ObjectiveHud : Control
{
    public abstract void Present(ObjectiveState objective);
}
