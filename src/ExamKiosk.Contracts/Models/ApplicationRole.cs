using System.Text.Json.Serialization;

namespace ExamKiosk.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<ApplicationRole>))]
public enum ApplicationRole
{
    [JsonStringEnumMemberName("primary")]
    Primary,

    [JsonStringEnumMemberName("helper")]
    Helper,
}
