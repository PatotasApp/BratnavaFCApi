using System.Text.Json.Serialization;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos;

public record PublishMatchEventRequest(
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    MatchEventType Type,
    int SecondsBeforeStart = 15,
    int DurationSeconds = 15,
    DateTimeOffset? EventTime = null
);
