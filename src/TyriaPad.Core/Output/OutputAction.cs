namespace TyriaPad.Core.Output;

public enum OutputKind : byte
{
    None,
    Key,
    Mouse,
}

/// <summary>Something that can be held down: a key or a mouse button.</summary>
public readonly record struct OutputAction
{
    private OutputAction(OutputKind kind, Key key, MouseButton button)
    {
        Kind = kind;
        Key = key;
        Button = button;
    }

    public static OutputAction None => default;

    public OutputKind Kind { get; }

    public Key Key { get; }

    public MouseButton Button { get; }

    public bool IsNone => Kind == OutputKind.None;

    public static OutputAction FromKey(Key key) => new(OutputKind.Key, key, default);

    public static OutputAction FromMouse(MouseButton button) => new(OutputKind.Mouse, default, button);

    public static implicit operator OutputAction(Key key) => FromKey(key);

    public static implicit operator OutputAction(MouseButton button) => FromMouse(button);

    public override string ToString() => Kind switch
    {
        OutputKind.Key => Key.ToString(),
        OutputKind.Mouse => $"Mouse{Button}",
        _ => "None",
    };
}