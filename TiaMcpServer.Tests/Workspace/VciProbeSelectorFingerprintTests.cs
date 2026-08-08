using System.Globalization;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Workspace;

/// <summary>
/// Pure unit tests for <see cref="VciProbeSelectorFingerprint"/> (VCI Workspace Phase 1 Task 4.1).
/// No Siemens Openness dependency — exercised directly against the linked source file, exactly like
/// the Task 3 <c>VciProbeValueNormalizer</c>/<c>VciProbeObservationRunner</c> tests.
/// </summary>
public class VciProbeSelectorFingerprintTests
{
    private static VciSelectorFingerprintPathSegment Segment(string kind, string name, int sameNameOrdinal = 0)
        => new() { Kind = kind, Name = name, SameNameOrdinal = sameNameOrdinal };

    private static VciSelectorFingerprintIdentityField Identity(string key, string value)
        => new() { Key = key, Value = value };

    private static VciSelectorFingerprintInput BaselineInput() => new()
    {
        SchemaVersion = "vci-read-probe/v1",
        RuntimeTypeName = "Siemens.Engineering.SW.Blocks.OB",
        StructuralPath = new()
        {
            Segment("Device", "PLC_1"),
            Segment("PlcSoftware", "PLC_1"),
            Segment("BlockFolder", "Program blocks"),
            Segment("OB", "Main", sameNameOrdinal: 0),
        },
        IdentityFields = new()
        {
            Identity("Name", "Main"),
            Identity("Number", "1"),
        },
    };

    [Fact]
    public void SerializeStructuralPath_ProducesOrdinalSegmentListWithKindNameSameNameOrdinal()
    {
        var path = new List<VciSelectorFingerprintPathSegment>
        {
            Segment("Device", "PLC_1"),
            Segment("OB", "Main", sameNameOrdinal: 2),
        };

        var serialized = VciProbeSelectorFingerprint.SerializeStructuralPath(path);

        Assert.Equal(
            "[{\"kind\":\"Device\",\"name\":\"PLC_1\",\"sameNameOrdinal\":0},"
            + "{\"kind\":\"OB\",\"name\":\"Main\",\"sameNameOrdinal\":2}]",
            serialized);
    }

    [Fact]
    public void SerializeStructuralPath_EmptyPathProducesEmptyArray()
    {
        var serialized = VciProbeSelectorFingerprint.SerializeStructuralPath(new List<VciSelectorFingerprintPathSegment>());

        Assert.Equal("[]", serialized);
    }

    [Fact]
    public void SerializeIdentityFields_ProducesOrdinalKeyValueList()
    {
        var fields = new List<VciSelectorFingerprintIdentityField>
        {
            Identity("Name", "Main"),
            Identity("Number", "1"),
        };

        var serialized = VciProbeSelectorFingerprint.SerializeIdentityFields(fields);

        Assert.Equal(
            "[{\"key\":\"Name\",\"value\":\"Main\"},{\"key\":\"Number\",\"value\":\"1\"}]",
            serialized);
    }

    [Fact]
    public void Compute_IsDeterministicForIdenticalInput()
    {
        var first = VciProbeSelectorFingerprint.Compute(BaselineInput());
        var second = VciProbeSelectorFingerprint.Compute(BaselineInput());

        Assert.Equal(first, second);
    }

    [Fact]
    public void Compute_ProducesLowercaseHexSha256()
    {
        var fingerprint = VciProbeSelectorFingerprint.Compute(BaselineInput());

        Assert.Equal(64, fingerprint.Length);
        Assert.Matches("^[0-9a-f]{64}$", fingerprint);
    }

    [Fact]
    public void Compute_IsUnaffectedByCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var invariant = VciProbeSelectorFingerprint.Compute(BaselineInput());

            // de-DE uses "," as the decimal separator and different digit grouping; if any
            // formatting in the fingerprint path were culture-sensitive this would diverge.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            var german = VciProbeSelectorFingerprint.Compute(BaselineInput());

            // tr-TR has the well-known "Turkish I" casing bug that trips up naive ToLower() calls.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var turkish = VciProbeSelectorFingerprint.Compute(BaselineInput());

            Assert.Equal(invariant, german);
            Assert.Equal(invariant, turkish);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void Compute_ChangesWhenASegmentNameChanges()
    {
        var baseline = VciProbeSelectorFingerprint.Compute(BaselineInput());

        var changed = BaselineInput();
        changed.StructuralPath[^1].Name = "OtherBlock";

        Assert.NotEqual(baseline, VciProbeSelectorFingerprint.Compute(changed));
    }

    [Fact]
    public void Compute_ChangesWhenASegmentKindChanges()
    {
        var baseline = VciProbeSelectorFingerprint.Compute(BaselineInput());

        var changed = BaselineInput();
        changed.StructuralPath[^1].Kind = "FB";

        Assert.NotEqual(baseline, VciProbeSelectorFingerprint.Compute(changed));
    }

    [Fact]
    public void Compute_ChangesWhenASameNameOrdinalChanges()
    {
        var baseline = VciProbeSelectorFingerprint.Compute(BaselineInput());

        var changed = BaselineInput();
        changed.StructuralPath[^1].SameNameOrdinal = 1;

        Assert.NotEqual(baseline, VciProbeSelectorFingerprint.Compute(changed));
    }

    [Fact]
    public void Compute_ChangesWhenRuntimeTypeChanges()
    {
        var baseline = VciProbeSelectorFingerprint.Compute(BaselineInput());

        var changed = BaselineInput();
        changed.RuntimeTypeName = "Siemens.Engineering.SW.Blocks.FB";

        Assert.NotEqual(baseline, VciProbeSelectorFingerprint.Compute(changed));
    }

    [Fact]
    public void Compute_ChangesWhenSchemaVersionChanges()
    {
        var baseline = VciProbeSelectorFingerprint.Compute(BaselineInput());

        var changed = BaselineInput();
        changed.SchemaVersion = "vci-read-probe/v2";

        Assert.NotEqual(baseline, VciProbeSelectorFingerprint.Compute(changed));
    }

    [Fact]
    public void Compute_ChangesWhenAnIdentityFieldValueChanges()
    {
        var baseline = VciProbeSelectorFingerprint.Compute(BaselineInput());

        var changed = BaselineInput();
        changed.IdentityFields[1].Value = "2";

        Assert.NotEqual(baseline, VciProbeSelectorFingerprint.Compute(changed));
    }

    [Fact]
    public void Compute_ChangesWhenAnIdentityFieldIsAdded()
    {
        var baseline = VciProbeSelectorFingerprint.Compute(BaselineInput());

        var changed = BaselineInput();
        changed.IdentityFields.Add(Identity("Comment", "extra"));

        Assert.NotEqual(baseline, VciProbeSelectorFingerprint.Compute(changed));
    }

    [Fact]
    public void Compute_ChangesWhenASegmentIsAppended()
    {
        var baseline = VciProbeSelectorFingerprint.Compute(BaselineInput());

        var changed = BaselineInput();
        changed.StructuralPath.Add(Segment("Extra", "Segment"));

        Assert.NotEqual(baseline, VciProbeSelectorFingerprint.Compute(changed));
    }

    [Fact]
    public void Compute_ThrowsOnNullInput()
    {
        Assert.Throws<ArgumentNullException>(() => VciProbeSelectorFingerprint.Compute(null!));
    }
}
