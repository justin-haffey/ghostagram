namespace Editor.Client.Components;

/// <summary>Describes whether the JavaScript canvas adapter is usable by shell controls.</summary>
public enum CanvasInitializationState
{
    Pending,
    Ready,
    Failed
}
