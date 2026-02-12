using System;
using System.Text.RegularExpressions;

namespace BratnavaFC.Domain.Entities;

public class TeamColorEntity : BaseEntity
{
    private static readonly Regex HexRegex = new(@"^#?[0-9A-Fa-f]{6}$", RegexOptions.Compiled);

    private TeamColorEntity() { } // EF

    public TeamColorEntity(Guid groupId, string name, string hexValue)
    {
        if (groupId == Guid.Empty)
            throw new InvalidOperationException("GroupId e obrigatorio.");

        GroupId = groupId;
        IsActive = true;

        SetName(name);
        SetHexValue(hexValue);
    }

    public Guid GroupId { get; private set; }
    public bool IsActive { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public string HexValue { get; private set; } = string.Empty;

    public void SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Nome da cor e obrigatorio.");

        Name = name.Trim();
    }

    public void SetHexValue(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            throw new InvalidOperationException("Hex da cor e obrigatorio.");

        hex = hex.Trim();

        if (!HexRegex.IsMatch(hex))
            throw new InvalidOperationException("Hex invalido. Use o formato #RRGGBB (ex: #1A2B3C).");

        if (!hex.StartsWith('#'))
            hex = "#" + hex;

        HexValue = hex.ToUpperInvariant();
    }

    public void Inactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
