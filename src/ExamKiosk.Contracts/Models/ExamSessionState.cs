using System.Text.Json.Serialization;

namespace ExamKiosk.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<ExamSessionState>))]
public enum ExamSessionState
{
    [JsonStringEnumMemberName("starting")]
    Starting,

    [JsonStringEnumMemberName("active")]
    Active,

    [JsonStringEnumMemberName("completing")]
    Completing,

    [JsonStringEnumMemberName("completed")]
    Completed,

    [JsonStringEnumMemberName("cancelled")]
    Cancelled,

    [JsonStringEnumMemberName("expired")]
    Expired,
}
