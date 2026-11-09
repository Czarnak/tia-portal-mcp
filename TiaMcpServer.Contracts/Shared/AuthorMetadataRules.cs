namespace TiaMcpServer.Contracts.Shared;

/// <summary>Author metadata admission shared by host and worker.</summary>
public static class AuthorMetadataRules
{
    public const int MaxLength = 255;

    public static string? Validate(string? author)
        => author?.Length > MaxLength
            ? $"author exceeds the maximum {MaxLength} UTF-16 code units; actual length is {author.Length}."
            : null;
}
