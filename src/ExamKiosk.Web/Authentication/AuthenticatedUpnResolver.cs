using System.Security.Claims;

namespace ExamKiosk.Web.Authentication;

public static class AuthenticatedUpnResolver
{
    public static string? Resolve(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return Normalize(
            user.FindFirstValue("preferred_username")
            ?? user.FindFirstValue(ClaimTypes.Upn));
    }

    private static string? Normalize(string? userPrincipalName)
    {
        if (string.IsNullOrWhiteSpace(userPrincipalName))
        {
            return null;
        }

        return userPrincipalName.Trim();
    }
}
