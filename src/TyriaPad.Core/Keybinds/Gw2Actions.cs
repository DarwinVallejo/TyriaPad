using TyriaPad.Core.Output;

namespace TyriaPad.Core.Keybinds;

/// <summary>
/// GW2 actions by the <c>id</c> in the InputBinds XML (it doesn't change with the language, unlike
/// <c>name</c>). Only the ones TyriaPad uses or that tend to clash with its keys; an id that isn't here
/// is still read and is named with the XML <c>name</c>.
/// </summary>
public enum Gw2Action
{
    MoveForward = 0,
    MoveBackward = 1,
    StrafeLeft = 2,
    StrafeRight = 3,
    TurnLeft = 4,
    TurnRight = 5,
    Dodge = 6,
    Autorun = 7,
    Walk = 8,
    Jump = 9,
    SwimUp = 10,
    SwimDown = 11,
    AboutFace = 12,
    FreeCamera = 13,
    ZoomIn = 14,
    ZoomOut = 15,
    ReverseCamera = 16,
    WeaponSwap = 17,
    Weapon1 = 18,
    Weapon2 = 19,
    Weapon3 = 20,
    Weapon4 = 21,
    Weapon5 = 22,
    Heal = 23,
    Utility1 = 24,
    Utility2 = 25,
    Utility3 = 26,
    Elite = 27,
    Profession1 = 28,
    Profession2 = 29,
    Profession3 = 30,
    Profession4 = 31,
    Profession5 = 32,
    Profession6 = 33,
    Profession7 = 34,
    SpecialAction = 35,
    NearestAlly = 37,
    NextAlly = 38,
    PreviousAlly = 39,
    TradingPost = 40,
    Contacts = 41,
    Guild = 42,
    Hero = 43,
    NearestEnemy = 44,
    NextEnemy = 45,
    PreviousEnemy = 46,
    LockTarget = 47,
    Inventory = 48,
    Pets = 49,
    Interact = 51,
    StowWeapons = 53,
    TakeTarget = 54,
    Party = 55,
    CallTarget = 57,
    Scoreboard = 58,
    Map = 65,
    Mail = 71,
    PvpPanel = 73,
    ActionCamera = 78,
    Mount = 152,
    Raptor = 155,
    Springer = 156,
    Skimmer = 157,
    Jackal = 158,
    Griffon = 159,
    RollerBeetle = 161,
    Warclaw = 169,
    Skyscale = 170,
    BuildTemplate1 = 171,
    BuildTemplate2 = 172,
    EquipmentTemplate1 = 182,
    EquipmentTemplate2 = 183,
    SiegeTurtle = 203,
    Fishing = 204,
    Skiff = 205,
    JadeBotWaypoint = 206,
    RiftScan = 207,
    SkyscaleLeap = 208,
    ConjuredDoorway = 211,
}

/// <summary>Name of an action and its default keys in GW2.</summary>
public sealed record Gw2ActionInfo(Gw2Action Action, string Name, Chord? Default = null, Chord? Default2 = null);

public static class Gw2Actions
{
    /// <summary>Skill bar slots 1–0, in order.</summary>
    public static readonly Gw2Action[] SkillSlots =
    [
        Gw2Action.Weapon1, Gw2Action.Weapon2, Gw2Action.Weapon3, Gw2Action.Weapon4, Gw2Action.Weapon5,
        Gw2Action.Heal, Gw2Action.Utility1, Gw2Action.Utility2, Gw2Action.Utility3, Gw2Action.Elite,
    ];

    /// <summary>Profession bar slots F1–F7 (GW2 has no eighth).</summary>
    public static readonly Gw2Action[] ProfessionSlots =
    [
        Gw2Action.Profession1, Gw2Action.Profession2, Gw2Action.Profession3, Gw2Action.Profession4,
        Gw2Action.Profession5, Gw2Action.Profession6, Gw2Action.Profession7,
    ];

    /// <summary>
    /// Client default keys (US keyboard), because the XML only contains what was changed. Actions
    /// whose default is unclear are left without a key: that way no conflicts are made up.
    /// </summary>
    private static readonly Gw2ActionInfo[] s_table =
    [
        new(Gw2Action.MoveForward, "Move Forward", Key.W, Key.Up),
        new(Gw2Action.MoveBackward, "Move Backward", Key.S, Key.Down),
        new(Gw2Action.StrafeLeft, "Strafe Left", Key.Q),
        new(Gw2Action.StrafeRight, "Strafe Right", Key.E),
        new(Gw2Action.TurnLeft, "Turn Left", Key.A, Key.Left),
        new(Gw2Action.TurnRight, "Turn Right", Key.D, Key.Right),
        new(Gw2Action.Dodge, "Dodge", Key.V),
        new(Gw2Action.Autorun, "Autorun", Key.R, Key.NumLock),
        new(Gw2Action.Walk, "Walk"),
        new(Gw2Action.Jump, "Jump", Key.Space),
        new(Gw2Action.SwimUp, "Swim Up", Key.Space),
        new(Gw2Action.SwimDown, "Swim Down", Key.C),
        new(Gw2Action.AboutFace, "About Face"),
        new(Gw2Action.FreeCamera, "Free Camera"),
        new(Gw2Action.ZoomIn, "Zoom In"),
        new(Gw2Action.ZoomOut, "Zoom Out"),
        new(Gw2Action.ReverseCamera, "Reverse Camera"),
        new(Gw2Action.WeaponSwap, "Weapon Swap", Key.Grave),
        new(Gw2Action.Weapon1, "Weapon Skill 1", Key.D1),
        new(Gw2Action.Weapon2, "Weapon Skill 2", Key.D2),
        new(Gw2Action.Weapon3, "Weapon Skill 3", Key.D3),
        new(Gw2Action.Weapon4, "Weapon Skill 4", Key.D4),
        new(Gw2Action.Weapon5, "Weapon Skill 5", Key.D5),
        new(Gw2Action.Heal, "Healing Skill", Key.D6),
        new(Gw2Action.Utility1, "Utility Skill 1", Key.D7),
        new(Gw2Action.Utility2, "Utility Skill 2", Key.D8),
        new(Gw2Action.Utility3, "Utility Skill 3", Key.D9),
        new(Gw2Action.Elite, "Elite Skill", Key.D0),
        new(Gw2Action.Profession1, "Profession Skill 1", Key.F1),
        new(Gw2Action.Profession2, "Profession Skill 2", Key.F2),
        new(Gw2Action.Profession3, "Profession Skill 3", Key.F3),
        new(Gw2Action.Profession4, "Profession Skill 4", Key.F4),
        new(Gw2Action.Profession5, "Profession Skill 5", Key.F5),
        new(Gw2Action.Profession6, "Profession Skill 6", Key.F6),
        new(Gw2Action.Profession7, "Profession Skill 7", Key.F7),
        new(Gw2Action.SpecialAction, "Special Action Key", Key.N),
        new(Gw2Action.NearestAlly, "Nearest Ally"),
        new(Gw2Action.NextAlly, "Next Ally"),
        new(Gw2Action.PreviousAlly, "Previous Ally"),
        new(Gw2Action.TradingPost, "Trading Post", Key.O),
        new(Gw2Action.Contacts, "Contacts", Key.Y),
        new(Gw2Action.Guild, "Guild", Key.G),
        new(Gw2Action.Hero, "Hero Panel", Key.H),
        new(Gw2Action.NearestEnemy, "Nearest Enemy"),
        new(Gw2Action.NextEnemy, "Next Enemy", Key.Tab),
        new(Gw2Action.PreviousEnemy, "Previous Enemy"),
        new(Gw2Action.LockTarget, "Lock Target"),
        new(Gw2Action.Inventory, "Inventory", Key.I),
        new(Gw2Action.Pets, "Pets", Key.K),
        new(Gw2Action.Interact, "Interact", Key.F),
        new(Gw2Action.StowWeapons, "Stow/Draw Weapons", Key.Z),
        new(Gw2Action.TakeTarget, "Take Target", Key.T),
        new(Gw2Action.Party, "Party", Key.P),
        new(Gw2Action.CallTarget, "Call Target", new Chord(KeyModifiers.Ctrl, Key.T)),
        new(Gw2Action.Scoreboard, "WvW Scoreboard", Key.B),
        new(Gw2Action.Map, "Map", Key.M),
        new(Gw2Action.Mail, "Mail Dialog"),
        new(Gw2Action.PvpPanel, "PvP Panel"),
        new(Gw2Action.ActionCamera, "Toggle Action Camera"),
        new(Gw2Action.Mount, "Mount/Dismount", Key.X),
        new(Gw2Action.Raptor, "Raptor Mount/Dismount"),
        new(Gw2Action.Springer, "Springer Mount/Dismount"),
        new(Gw2Action.Skimmer, "Skimmer Mount/Dismount"),
        new(Gw2Action.Jackal, "Jackal Mount/Dismount"),
        new(Gw2Action.Griffon, "Griffon Mount/Dismount"),
        new(Gw2Action.RollerBeetle, "Roller Beetle Mount/Dismount"),
        new(Gw2Action.Warclaw, "Warclaw Mount/Dismount"),
        new(Gw2Action.Skyscale, "Skyscale Mount/Dismount"),
        new(Gw2Action.SiegeTurtle, "Siege Turtle Mount/Dismount"),
        new(Gw2Action.BuildTemplate1, "Build Template 1"),
        new(Gw2Action.BuildTemplate2, "Build Template 2"),
        new(Gw2Action.EquipmentTemplate1, "Equipment Template 1"),
        new(Gw2Action.EquipmentTemplate2, "Equipment Template 2"),
        new(Gw2Action.Fishing, "Start Fishing"),
        new(Gw2Action.Skiff, "Summon Skiff"),
        new(Gw2Action.JadeBotWaypoint, "Set Jade Bot Waypoint"),
        new(Gw2Action.RiftScan, "Scan for Rift"),
        new(Gw2Action.SkyscaleLeap, "Skyscale Leap"),
        new(Gw2Action.ConjuredDoorway, "Conjured Doorway"),
    ];

    private static readonly Dictionary<Gw2Action, Gw2ActionInfo> s_byAction = s_table.ToDictionary(static a => a.Action);

    public static IReadOnlyList<Gw2ActionInfo> All => s_table;

    public static Gw2ActionInfo? Find(Gw2Action action) => s_byAction.GetValueOrDefault(action);

    /// <summary>Name for the warnings; unknown ids are named with whatever the XML says.</summary>
    public static string NameOf(Gw2Action action, string? xmlName = null)
        => Find(action)?.Name ?? (string.IsNullOrWhiteSpace(xmlName) ? $"action #{(int)action}" : xmlName);
}
