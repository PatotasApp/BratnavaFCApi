namespace BratnavaFC.Application.Statistics;

/// <summary>
/// Nome curto de exibição: só o primeiro nome, a não ser que outro jogador da patota
/// (convidado também) tenha o mesmo primeiro nome; aí vai o nome completo.
/// </summary>
public static class ShortNames
{
    public static Dictionary<Guid, string> Build(IEnumerable<(Guid Id, string Name)> players)
    {
        var list = players.Select(p => (p.Id, Name: (p.Name ?? "").Trim(), First: FirstName(p.Name))).ToList();
        var repeated = list
            .GroupBy(p => p.First, StringComparer.CurrentCultureIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        return list.ToDictionary(p => p.Id, p => repeated.Contains(p.First) ? p.Name : p.First);
    }

    private static string FirstName(string? name)
    {
        var trimmed = (name ?? "").Trim();
        var space = trimmed.IndexOf(' ');
        return space < 0 ? trimmed : trimmed[..space];
    }
}
