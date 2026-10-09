namespace TiaMcpServer.Contracts.Hmi;

/// <summary>
/// Result of <c>validate</c>: one page of the scanned objects of a category. <see cref="Scanned"/> counts the
/// objects of the page, <see cref="Clean"/> those validated without a finding; only objects with errors or
/// warnings are listed in <see cref="Findings"/>. <see cref="NotValidatable"/> lists once each object kind
/// of the page that has no <c>Validate</c>.
/// </summary>
public class HmiValidationInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    public HmiPage Page { get; set; } = new HmiPage(0, 0, 0, null);

    public int Scanned { get; set; }

    public int Clean { get; set; }

    public List<HmiValidationFindingInfo> Findings { get; set; } = new List<HmiValidationFindingInfo>();

    public List<string> NotValidatable { get; set; } = new List<string>();
}

/// <summary>An object whose validation reported errors or warnings.</summary>
public class HmiValidationFindingInfo
{
    public string ObjectKind { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public List<HmiValidationResultInfo> Results { get; set; } = new List<HmiValidationResultInfo>();
}

/// <summary>One validation result of an object; <see cref="PropertyName"/> is null when unreadable.</summary>
public class HmiValidationResultInfo
{
    public string? PropertyName { get; set; }

    public List<string> Errors { get; set; } = new List<string>();

    public List<string> Warnings { get; set; } = new List<string>();
}
