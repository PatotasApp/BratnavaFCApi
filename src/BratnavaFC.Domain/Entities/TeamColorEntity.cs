using System.Text.RegularExpressions;

namespace BratnavaFC.Domain.Entities;

public class TeamColorEntity : BaseEntity
{
    private static readonly Regex HexRegex = new("^#(?:[0-9a-fA-F]{6})$", RegexOptions.Compiled);

    // EF Core
    private TeamColorEntity() { }

    public TeamColorEntity(string name, string hexValue)
    {
        SetName(name);
        SetHexValue(hexValue);
    }

    public string Name { get; private set; } = string.Empty;
    public string HexValue { get; private set; } = string.Empty;

    public void Update(string name, string hexValue)
    {
        SetName(name);
        SetHexValue(hexValue);
    }

    private void SetName(string name)
    {
        name = (name ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Nome da cor é obrigatório.");

        if (name.Length > 40)
            throw new InvalidOperationException("Nome da cor deve ter no máximo 40 caracteres.");

        Name = name;
    }

    private void SetHexValue(string hexValue)
    {
        hexValue = (hexValue ?? string.Empty).Trim();

        // aceita #RRGGBB (mais consistente pra UI)
        if (!HexRegex.IsMatch(hexValue))
            throw new InvalidOperationException("HexValue inválido. Use o formato #RRGGBB (ex: #12AB34).");

        HexValue = hexValue.ToUpperInvariant();
    }
}