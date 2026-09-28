using TyriaPad.Core.Input;
using TyriaPad.Core.Mapping;
using TyriaPad.Core.Output;

namespace TyriaPad.Core.Tests;

public class LayerEngineTests
{
    private const GamepadButtons LB = GamepadButtons.LeftBumper;
    private const GamepadButtons LT = GamepadButtons.LeftTrigger;
    private const GamepadButtons RT = GamepadButtons.RightTrigger;

    private readonly LayerEngine _engine = new(new ContextLayout(
        GameContext.Combat,
        StickRole.Move,
        StickRole.Camera,
        [
            MakeLayer(GamepadButtons.None),
            MakeLayer(LB),
            MakeLayer(LT, right: StickRole.None),
            MakeLayer(LT | RT),
        ]));

    [Fact]
    public void NothingHeld_IsBase()
    {
        Assert.Equal("base", _engine.Current.Name);
        Assert.False(_engine.Update(GamepadButtons.None));
    }

    [Fact]
    public void MostSpecificLayerWins()
    {
        Assert.True(_engine.Update(LT));
        Assert.Equal("LT", _engine.Current.Name);

        Assert.True(_engine.Update(LT | RT));
        Assert.Equal("LT+RT", _engine.Current.Name);

        Assert.True(_engine.Update(RT));
        Assert.Equal("base", _engine.Current.Name);
    }

    [Fact]
    public void Tie_KeepsCurrentLayer_InEitherOrder()
    {
        _engine.Update(LB);
        Assert.False(_engine.Update(LB | LT));
        Assert.Equal("LB", _engine.Current.Name);

        _engine.Update(GamepadButtons.None);
        _engine.Update(LT);
        Assert.False(_engine.Update(LT | LB));
        Assert.Equal("LT", _engine.Current.Name);
    }

    [Fact]
    public void IsModifierPress_OnlyWhenLayerWouldChange()
    {
        Assert.True(_engine.IsModifierPress(LB, GamepadButtons.None));
        Assert.False(_engine.IsModifierPress(RT, GamepadButtons.None));
        Assert.False(_engine.IsModifierPress(GamepadButtons.A, GamepadButtons.None));

        _engine.Update(LT);
        Assert.True(_engine.IsModifierPress(RT, LT));
        Assert.False(_engine.IsModifierPress(LB, LT));
    }

    [Fact]
    public void StickRoles_FallBackToContext()
    {
        Assert.Equal(StickRole.Camera, _engine.RightStick);
        _engine.Update(LT);
        Assert.Equal(StickRole.None, _engine.RightStick);
        Assert.Equal(StickRole.Move, _engine.LeftStick);
    }

    [Fact]
    public void ExtraButtonsHeld_DoNotMatter()
    {
        Assert.True(_engine.Update(LB | GamepadButtons.A | GamepadButtons.X));
        Assert.Equal("LB", _engine.Current.Name);
    }

    private static Layer MakeLayer(GamepadButtons modifiers, StickRole? right = null)
        => new(KeyNames.LayerName(modifiers), modifiers, null, right, new Dictionary<GamepadButtons, ButtonBinding>
        {
            [GamepadButtons.A] = new(Press: new ChordAction(new Chord(Key.Space))),
        });
}
