using System.Text.RegularExpressions;
using XIV.fm.Contracts.V1;

namespace XIV.fm.Server.Application.Sync;

public static partial class SyncRequestValidator
{
    private const int MaximumCharacterNameLength = 64;
    private const int MaximumPluginVersionLength = 32;
    private const int MaximumSnapshotVersionLength = 128;
    private const int MaximumSelectedRelays = 5;

    public static IReadOnlyDictionary<string, string[]> Validate(SyncRequest? request)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (request is null)
        {
            Add(errors, "request", "A JSON request body is required.");
            return Freeze(errors);
        }

        ValidatePluginVersion(request.PluginVersion, errors);
        ValidateCharacter(request.Character, errors);
        ValidateLocation(request.Location, errors);
        ValidateVisibility(request.Visibility, errors);
        ValidateSnapshotVersion(request.KnownSnapshotVersion, errors);
        return Freeze(errors);
    }

    private static void ValidatePluginVersion(string? pluginVersion, Dictionary<string, List<string>> errors)
    {
        if (string.IsNullOrWhiteSpace(pluginVersion))
        {
            Add(errors, "pluginVersion", "Plugin version is required.");
            return;
        }

        if (pluginVersion.Length > MaximumPluginVersionLength || !PluginVersionPattern().IsMatch(pluginVersion))
            Add(errors, "pluginVersion", "Plugin version must contain three or four numeric components.");
    }

    private static void ValidateCharacter(CharacterIdentity? character, Dictionary<string, List<string>> errors)
    {
        if (character is null)
        {
            Add(errors, "character", "Character identity is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(character.Name))
            Add(errors, "character.name", "Character name is required.");
        else
        {
            if (character.Name.Length > MaximumCharacterNameLength)
                Add(errors, "character.name", $"Character name cannot exceed {MaximumCharacterNameLength} characters.");
            if (!string.Equals(character.Name, character.Name.Trim(), StringComparison.Ordinal))
                Add(errors, "character.name", "Character name must already be trimmed.");
            if (character.Name.Any(char.IsControl))
                Add(errors, "character.name", "Character name cannot contain control characters.");
        }

        if (character.HomeWorldId == 0)
            Add(errors, "character.homeWorldId", "Home world ID must be non-zero.");
    }

    private static void ValidateLocation(LocationScope? location, Dictionary<string, List<string>> errors)
    {
        if (location is null)
        {
            Add(errors, "location", "Location is required.");
            return;
        }

        if (location.CurrentWorldId == 0)
            Add(errors, "location.currentWorldId", "Current world ID must be non-zero.");
        if (location.TerritoryId == 0)
            Add(errors, "location.territoryId", "Territory ID must be non-zero.");
        if (location.MapId == 0)
            Add(errors, "location.mapId", "Map ID must be non-zero.");
    }

    private static void ValidateVisibility(VisibilitySelection? visibility, Dictionary<string, List<string>> errors)
    {
        if (visibility is null)
        {
            Add(errors, "visibility", "Visibility selection is required.");
            return;
        }

        if (!Enum.IsDefined(visibility.Mode))
            Add(errors, "visibility.mode", "Visibility mode is invalid.");

        if (visibility.RelayIds is null)
        {
            Add(errors, "visibility.relayIds", "Relay IDs are required, even when empty.");
            return;
        }

        if (visibility.RelayIds.Count > MaximumSelectedRelays)
            Add(errors, "visibility.relayIds", $"No more than {MaximumSelectedRelays} Relays may be selected.");
        if (visibility.RelayIds.Any(id => id == Guid.Empty))
            Add(errors, "visibility.relayIds", "Relay IDs cannot be empty UUIDs.");
        if (visibility.RelayIds.Distinct().Count() != visibility.RelayIds.Count)
            Add(errors, "visibility.relayIds", "Relay IDs must be unique.");

        if (visibility.Mode is VisibilityMode.Private or VisibilityMode.Public)
        {
            if (visibility.RelayIds.Count != 0)
                Add(errors, "visibility.relayIds", "Private and Public visibility require an empty Relay list.");
        }
        else if (visibility.Mode == VisibilityMode.Custom && visibility.RelayIds.Count == 0)
        {
            Add(errors, "visibility.relayIds", "Custom visibility requires at least one Relay.");
        }
    }

    private static void ValidateSnapshotVersion(string? version, Dictionary<string, List<string>> errors)
    {
        if (version is null)
            return;

        if (string.IsNullOrWhiteSpace(version) || version.Length > MaximumSnapshotVersionLength)
            Add(errors, "knownSnapshotVersion", "Known snapshot version must contain 1 to 128 characters.");
    }

    private static void Add(Dictionary<string, List<string>> errors, string field, string message)
    {
        if (!errors.TryGetValue(field, out var messages))
        {
            messages = [];
            errors[field] = messages;
        }

        messages.Add(message);
    }

    private static Dictionary<string, string[]> Freeze(Dictionary<string, List<string>> errors) =>
        errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);

    [GeneratedRegex("^[0-9]+\\.[0-9]+\\.[0-9]+(?:\\.[0-9]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex PluginVersionPattern();
}
