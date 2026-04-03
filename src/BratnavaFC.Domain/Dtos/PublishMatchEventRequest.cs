using System.Text.Json.Serialization;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos;

public record PublishMatchEventRequest(
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    MatchEventType Type,
    int DurationSeconds = 20
);
