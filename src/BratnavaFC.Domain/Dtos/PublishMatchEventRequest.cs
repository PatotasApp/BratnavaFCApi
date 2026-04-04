using System.Text.Json.Serialization;
using BratnavaFC.Domain.Enums;

namespace BratnavaFC.Domain.Dtos;

public record PublishMatchEventRequest(
    [property: JsonConverter(typeof(JsonStringEnumConverter))]
    MatchEventType Type,
    DateTime EventTime,
    int SecondsBeforeStart,
    int DurationSeconds = 20
);
