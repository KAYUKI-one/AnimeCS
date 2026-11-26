namespace AnimeCS.Data;

internal sealed record PlayerProfile(string[] Inventory, Dictionary<string, string> Equipment, int Victories, string? AvatarId)
{
    internal static bool ValidId(string? id) => id is { Length: > 0 and <= 96 } &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    internal bool Valid()
    {
        if (Inventory is not { Length: <= 4096 } || Equipment is not { Count: <= 64 } ||
            Victories < 0 || AvatarId != null && !ValidId(AvatarId)) return false;
        var owned = new HashSet<string>(StringComparer.Ordinal);
        return Inventory.All(id => ValidId(id) && owned.Add(id)) &&
            Equipment.All(pair => ValidId(pair.Key) && ValidId(pair.Value) && owned.Contains(pair.Value));
    }
}

internal sealed record PlayerRecord(string Id, long Revision, PlayerProfile Profile);
