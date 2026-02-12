using System;

namespace BratnavaFC.Domain.Entities
{
    public class TeamColorEntity : BaseEntity
    {
        // EF Core 
        private TeamColorEntity() { }

        public TeamColorEntity(string name, string hexValue)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            HexValue = hexValue ?? throw new ArgumentNullException(nameof(hexValue));
        }

        public string Name { get; private set; } = string.Empty;
        public string HexValue { get; private set; } = string.Empty;

        public void SetName(string name)
        {
            Name = name ?? string.Empty;
            UpdateDate = DateTime.UtcNow;
        }

        public void SetHexValue(string hex)
        {
            HexValue = hex ?? string.Empty;
            UpdateDate = DateTime.UtcNow;
        }
    }
}
