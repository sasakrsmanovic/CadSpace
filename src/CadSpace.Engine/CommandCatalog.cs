namespace CadSpace.Engine;

/// <summary>Deterministic command completion shared by command boxes and searchable tool palettes.</summary>
public static class CommandCatalog
{
    public static IReadOnlyList<CommandInfo> Suggest(string query, int maximum = 6)
    {
        ArgumentNullException.ThrowIfNull(query);
        if(maximum<1 || maximum>100) throw new ArgumentOutOfRangeException(nameof(maximum));
        query=query.Trim(); if(query.Length==0) return [];
        int Rank(CommandInfo c) => c.Name.Equals(query,StringComparison.OrdinalIgnoreCase)?0:c.Alias.Equals(query,StringComparison.OrdinalIgnoreCase)?1:c.Name.StartsWith(query,StringComparison.OrdinalIgnoreCase)?2:c.Alias.StartsWith(query,StringComparison.OrdinalIgnoreCase)?3:c.Name.Contains(query,StringComparison.OrdinalIgnoreCase)?4:c.Description.Contains(query,StringComparison.OrdinalIgnoreCase)?5:99;
        return CommandEngine.Commands.Select(c=>(Command:c,Score:Rank(c))).Where(p=>p.Score<99).OrderBy(p=>p.Score).ThenBy(p=>p.Command.Name,StringComparer.Ordinal).Take(maximum).Select(p=>p.Command).ToArray();
    }
}
