using System.Security.Cryptography;
using System.Text.Json;

namespace ExamKiosk.Contracts;

public static class EffectiveProfileDigest
{
    public static string Compute(EffectiveExamProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var profileJson = JsonSerializer.SerializeToUtf8Bytes(
            profile,
            AgentProtocol.SerializerOptions);
        return Convert.ToHexStringLower(SHA256.HashData(profileJson));
    }
}
