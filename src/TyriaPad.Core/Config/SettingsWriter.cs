using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TyriaPad.Core.Config;

/// <summary>
/// Writes into the text of <c>config.json</c> only the settings that changed, with
/// <see cref="JsonTextEditor"/>: the comments and whatever the user didn't touch stay the same.
/// </summary>
public static class SettingsWriter
{
    /// <summary>Text of config.json with the values of <paramref name="after"/> that differ from <paramref name="before"/>.</summary>
    public static string Apply(string json, TyriaPadSettings before, TyriaPadSettings after)
    {
        foreach ((string path, object? value) in Diff(before, after, string.Empty))
        {
            json = JsonTextEditor.Set(json, path, value);
        }

        return json;
    }

    /// <summary>JSON paths (camelCase, dotted) and new values of what changed.</summary>
    public static IEnumerable<(string Path, object? Value)> Diff(object before, object after, string prefix)
    {
        foreach (PropertyInfo property in before.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || property.GetCustomAttribute<JsonIgnoreAttribute>() is not null)
            {
                continue;
            }

            string path = prefix + JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            object? oldValue = property.GetValue(before);
            object? newValue = property.GetValue(after);
            if (IsSection(property.PropertyType) && oldValue is not null && newValue is not null)
            {
                foreach ((string, object?) change in Diff(oldValue, newValue, path + "."))
                {
                    yield return change;
                }
            }
            else if (!Equals(oldValue, newValue))
            {
                yield return (path, newValue);
            }
        }
    }

    // Sections are the nested settings records (gestures, camera, overlay…).
    private static bool IsSection(Type type)
        => type.IsClass && type != typeof(string) && type.Namespace?.StartsWith("TyriaPad.Core", StringComparison.Ordinal) == true;
}
