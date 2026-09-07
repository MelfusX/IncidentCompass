namespace IncidentCompass.Application.Governance.Tools;

public static class AgentToolIdentity
{
    public const int MaximumCharacters = 128;

    public static bool IsValid(string? value) =>
        value is { Length: >= 1 and <= MaximumCharacters } && value.All(IsSafeCharacter);

    private static bool IsSafeCharacter(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.';
}
