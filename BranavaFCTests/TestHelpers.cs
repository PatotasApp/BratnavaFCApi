using BratnavaFC.Domain.Dtos;

namespace BranavaFC.Tests;

internal static class TestHelpers
{
    public static List<PlayerRequestDto> Players(params (string name, bool gk)[] specs)
        => specs.Select((s, i) => new PlayerRequestDto(GuidFromInt(i + 1), s.name, s.gk)).ToList();

    public static Guid GuidFromInt(int n)
    {
        var bytes = new byte[16];
        bytes[15] = (byte)(n & 0xFF);
        bytes[14] = (byte)(n >> 8 & 0xFF);
        bytes[13] = (byte)(n >> 16 & 0xFF);
        bytes[12] = (byte)(n >> 24 & 0xFF);
        return new Guid(bytes);
    }
}
