using System.Reflection;

using TyriaPad.Core.Mapping;

namespace TyriaPad.Core.Config;

/// <summary>Configuration and profile built into the executable (the same files as in the repository).</summary>
public static class Defaults
{
    public const string ProfileName = "blaggletoad";

    private const string ConfigResource = "TyriaPad.Defaults.config.json";
    private const string ProfileResource = "TyriaPad.Defaults.profiles.blaggletoad.json";

    public static string ConfigJson => Read(ConfigResource);

    public static string ProfileJson => Read(ProfileResource);

    // SHA-256 (LF line breaks, no BOM) of the default profile of previous versions. When making
    // changes to profiles/blaggletoad.json, add here the fingerprint of the version being replaced.
    private static readonly HashSet<string> s_previousProfiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "dcff6e532871b55ab5c6a93ab541e95961cc78a99931d5b7875dd8e1b4e2ff7b", // phases 2–4 (no M1/M2)
        "0ffb71b055873204996354aae534dc125f69f62e4699cd620e9c99d66f256063", // phases 5–6
    };

    /// <summary>
    /// The text is an untouched default profile from a previous version: it can be replaced with the
    /// current one without losing the user's changes.
    /// </summary>
    public static bool IsPreviousDefaultProfile(string content) => s_previousProfiles.Contains(Fingerprint(content));

    public static string Fingerprint(string content)
    {
        string normalized = content.TrimStart('﻿').Replace("\r\n", "\n", StringComparison.Ordinal);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)));
    }

    public static LoadedConfig Load()
    {
        TyriaPadSettings settings = ConfigStore.ParseSettings(ConfigJson, "config.json (built-in)");
        Profile profile = ProfileParser.Parse(ProfileJson);
        return new LoadedConfig(settings, profile, "built-in");
    }

    private static string Read(string name)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing resource {name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
