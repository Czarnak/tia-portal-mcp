using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Cursors;
using Xunit;

namespace TiaMcpServer.Tests.Cursors;

public class AuthenticatedCursorProtectorTests
{
    private const string ProcessInstanceId = "process-a";
    private const string Purpose = "project-tree";
    private static readonly byte[] TestKey = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
    private static readonly byte[] OtherKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();

    [Fact]
    public void ProtectUnprotect_WithInjectedKeyAndProcessId_IsDeterministicAndRoundTrips()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);
        var state = new TestState("value");

        var first = protector.Protect(Purpose, state);
        var second = protector.Protect(Purpose, state);
        var result = protector.Unprotect<TestState>(Purpose, first);

        Assert.Equal(first, second);
        Assert.Equal(
            "{\"formatVersion\":1,\"processInstanceId\":\"process-a\",\"purpose\":\"project-tree\",\"state\":{\"value\":\"value\"}}",
            DecodePayload(first));
        Assert.DoesNotContain("=", first);
        Assert.Equal(1, first.Count(character => character == '.'));
        Assert.Equal(AuthenticatedCursorStatus.Success, result.Status);
        Assert.Equal(state, result.State);
    }

    [Fact]
    public void Unprotect_RejectsAnyPayloadOrSignatureByteChange()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);
        var parts = protector.Protect(Purpose, new TestState("value")).Split('.');

        Assert.Throws<AuthenticatedCursorException>(() =>
            protector.Unprotect<TestState>(Purpose, $"{ChangeByte(parts[0])}.{parts[1]}"));
        Assert.Throws<AuthenticatedCursorException>(() =>
            protector.Unprotect<TestState>(Purpose, $"{parts[0]}.{ChangeByte(parts[1])}"));
    }

    [Fact]
    public void PurposeSeparation_RejectsAValidCursorFromAnotherDomain()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);
        var cursor = protector.Protect("hardware-page", new TestState("value"));

        Assert.Throws<AuthenticatedCursorException>(() =>
            protector.Unprotect<TestState>(Purpose, cursor));
    }

    [Theory]
    [InlineData("")]
    [InlineData("%%%.AAAA")]
    [InlineData("one-part")]
    [InlineData("one.two.three")]
    [InlineData(".signature")]
    [InlineData("payload.")]
    [InlineData("cGF5bG9hZA==.c2lnbmF0dXJl")]
    public void Unprotect_RejectsMalformedOrPaddedBase64UrlFraming(string cursor)
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);

        Assert.Throws<AuthenticatedCursorException>(() =>
            protector.Unprotect<TestState>(Purpose, cursor));
    }

    [Fact]
    public void Unprotect_RejectsNonCanonicalBase64UrlPadBits()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);
        var parts = protector.Protect(Purpose, new TestState("value")).Split('.');

        Assert.Throws<AuthenticatedCursorException>(() =>
            protector.Unprotect<TestState>(Purpose, $"{parts[0]}.{ChangeUnusedPadBits(parts[1])}"));
    }

    [Theory]
    [MemberData(nameof(NonCanonicalEnvelopes))]
    public void Unprotect_RejectsCorrectlySignedNonCanonicalOrStructurallyInvalidEnvelope(string payload)
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);

        Assert.Throws<AuthenticatedCursorException>(() =>
            protector.Unprotect<TestState>(Purpose, Sign(payload, TestKey)));
    }

    [Fact]
    public void Unprotect_RejectsCorrectlySignedInvalidUtf8()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);
        var invalidUtf8 = new byte[] { 0xC3, 0x28 };

        Assert.Throws<AuthenticatedCursorException>(() =>
            protector.Unprotect<TestState>(Purpose, Sign(invalidUtf8, TestKey)));
    }

    [Theory]
    [InlineData(4095)]
    [InlineData(4096)]
    public void ProtectUnprotect_AcceptsSignedCursorsAtTheLengthBoundary(int cursorChars)
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);
        var (state, signedCursor) = BoundaryCursor(cursorChars);

        Assert.Equal(cursorChars, signedCursor.Length);
        Assert.Equal(state, protector.Unprotect<TestState>(Purpose, signedCursor).State);
        var produced = protector.Protect(Purpose, state);
        Assert.Equal(signedCursor, produced);
        Assert.Equal(state, protector.Unprotect<TestState>(Purpose, produced).State);
    }

    [Fact]
    public void Unprotect_RejectsOtherwiseValidSignedCursorAboveTheLengthBoundary()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);
        // Unpadded Base64URL skips lengths congruent to 1 mod 4, so 4097 is unattainable.
        var (_, signedCursor) = BoundaryCursor(4098);

        Assert.Throws<AuthenticatedCursorException>(() =>
            protector.Unprotect<TestState>(Purpose, signedCursor));
    }

    [Fact]
    public void Protect_RejectsStateWhoseSignedCursorCannotBeConsumed()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);
        var (state, signedCursor) = BoundaryCursor(4098);
        Assert.Equal(4098, signedCursor.Length);

        Assert.ThrowsAny<InvalidOperationException>(() => protector.Protect(Purpose, state));
    }

    [Fact]
    public void ForeignProcess_IsClassifiedBeforeDomainStateIsAccepted()
    {
        using var issuer = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);
        using var reader = new AuthenticatedCursorProtector(OtherKey, "process-b");
        var cursor = issuer.Protect(Purpose, new IncompatibleState(42));

        var result = reader.Unprotect<TestState>(Purpose, cursor);

        Assert.Equal(AuthenticatedCursorStatus.ForeignProcess, result.Status);
        Assert.Null(result.State);
    }

    [Fact]
    public void CurrentProcessCursor_WithBadSignature_IsRejected()
    {
        using var issuer = new AuthenticatedCursorProtector(OtherKey, ProcessInstanceId);
        using var reader = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);
        var cursor = issuer.Protect(Purpose, new TestState("value"));

        Assert.Throws<AuthenticatedCursorException>(() =>
            reader.Unprotect<TestState>(Purpose, cursor));
    }

    [Fact]
    public void Dispose_ZeroesOwnedKeyAndRejectsLaterOperations()
    {
        var callerKey = (byte[])TestKey.Clone();
        var protector = new AuthenticatedCursorProtector(callerKey, ProcessInstanceId);
        var cursor = protector.Protect(Purpose, new TestState("value"));
        var ownedKey = Assert.IsType<byte[]>(typeof(AuthenticatedCursorProtector)
            .GetField("_key", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(protector));

        protector.Dispose();

        Assert.All(ownedKey, value => Assert.Equal(0, value));
        Assert.Equal(TestKey, callerKey);
        Assert.Throws<ObjectDisposedException>(() => protector.Protect(Purpose, new TestState("value")));
        Assert.Throws<ObjectDisposedException>(() => protector.Unprotect<TestState>(Purpose, cursor));
    }

    private static readonly CursorKind TestKind = new(
        Purpose,
        "The supplied test cursor is invalid.",
        WorkerFailureCategories.SnapshotUnavailable);

    private static bool IsValidTestState(TestState state) => state.Value != "invalid";

    [Fact]
    public void SealOpen_RoundTripsAValidState()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);
        var state = new TestState("value");

        var cursor = protector.Seal(TestKind, state, IsValidTestState);

        Assert.Equal(state, protector.Open<TestState>(TestKind, cursor, IsValidTestState));
    }

    [Fact]
    public void Open_ForeignProcess_UsesTheKindsCategoryAndMessage()
    {
        using var issuer = new AuthenticatedCursorProtector(TestKey, "process-b");
        using var reader = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);
        var cursor = issuer.Seal(TestKind, new TestState("value"), IsValidTestState);

        var exception = Assert.Throws<CursorException>(() =>
            reader.Open<TestState>(TestKind, cursor, IsValidTestState));

        Assert.Equal(WorkerFailureCategories.SnapshotUnavailable, exception.Category);
        Assert.Equal(TestKind.InvalidMessage, exception.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Open_TamperedOrInvalidState_IsInvalidCursor(bool tamper)
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);
        var cursor = protector.Seal(TestKind, new TestState("value"), IsValidTestState);
        var parts = cursor.Split('.');
        var supplied = tamper ? $"{ChangeByte(parts[0])}.{parts[1]}" : cursor;
        Func<TestState, bool> isValid = tamper ? IsValidTestState : _ => false;

        var exception = Assert.Throws<CursorException>(() =>
            protector.Open(TestKind, supplied, isValid));

        Assert.Equal(WorkerFailureCategories.InvalidCursor, exception.Category);
        Assert.Equal(TestKind.InvalidMessage, exception.Message);
    }

    [Fact]
    public void Seal_InvalidState_IsInvalidCursorWithKindMessage()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);

        var exception = Assert.Throws<CursorException>(() =>
            protector.Seal(TestKind, new TestState("invalid"), IsValidTestState));

        Assert.Equal(WorkerFailureCategories.InvalidCursor, exception.Category);
        Assert.Equal(TestKind.InvalidMessage, exception.Message);
    }

    [Fact]
    public void Seal_Oversized_KeepsSizeDataAsInnerException()
    {
        using var protector = new AuthenticatedCursorProtector(TestKey, ProcessInstanceId);
        var (state, _) = BoundaryCursor(4098);

        var exception = Assert.Throws<CursorException>(() =>
            protector.Seal(TestKind, state, IsValidTestState));

        Assert.Equal(WorkerFailureCategories.InvalidCursor, exception.Category);
        var size = Assert.IsType<AuthenticatedCursorSizeException>(exception.InnerException);
        Assert.Equal(4096, size.LimitChars);
        Assert.True(size.CursorChars > 4096);
    }

    public static IEnumerable<object[]> NonCanonicalEnvelopes()
    {
        const string canonical =
            "{\"formatVersion\":1,\"processInstanceId\":\"process-a\",\"purpose\":\"project-tree\",\"state\":{\"value\":\"value\"}}";
        yield return new object[]
        {
            "{\"processInstanceId\":\"process-a\",\"formatVersion\":1,\"purpose\":\"project-tree\",\"state\":{\"value\":\"value\"}}",
        };
        yield return new object[] { "{ " + canonical[1..] };
        yield return new object[] { canonical.Replace("process-a", "\\u0070rocess-a", StringComparison.Ordinal) };
        yield return new object[] { canonical[..^1] + ",\"purpose\":\"project-tree\"}" };
        yield return new object[] { canonical[..^1] + ",\"extra\":true}" };
        yield return new object[] { canonical.Replace(",\"purpose\":\"project-tree\"", string.Empty, StringComparison.Ordinal) };
        yield return new object[] { canonical.Replace("\"formatVersion\":1", "\"formatVersion\":2", StringComparison.Ordinal) };
        yield return new object[] { canonical.Replace("\"processInstanceId\":\"process-a\"", "\"processInstanceId\":\"\"", StringComparison.Ordinal) };
        yield return new object[] { canonical + "{}" };
    }

    private static (TestState State, string Cursor) BoundaryCursor(int cursorChars)
    {
        const string prefix = "{\"formatVersion\":1,\"processInstanceId\":\"process-a\",\"purpose\":\"project-tree\",\"state\":{\"value\":\"";
        const string suffix = "\"}}";
        // Derive ASCII payload size independently of Protect; each extra byte changes framing length.
        var valueChars = Enumerable.Range(0, cursorChars).Single(count =>
            ((prefix.Length + count + suffix.Length) * 8 + 5) / 6 + 1 + 43 == cursorChars);
        var value = new string('x', valueChars);
        return (new TestState(value), Sign(prefix + value + suffix, TestKey));
    }

    private static string DecodePayload(string cursor)
        => Encoding.UTF8.GetString(DecodeBase64Url(cursor.Split('.')[0]));

    private static string Sign(string payload, byte[] key)
        => Sign(Encoding.UTF8.GetBytes(payload), key);

    private static string Sign(byte[] payload, byte[] key)
    {
        using var hmac = new HMACSHA256(key);
        return $"{EncodeBase64Url(payload)}.{EncodeBase64Url(hmac.ComputeHash(payload))}";
    }

    private static string ChangeByte(string value)
        => (value[0] == 'A' ? "B" : "A") + value[1..];

    private static string ChangeUnusedPadBits(string value)
    {
        const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var index = Alphabet.IndexOf(value[^1], StringComparison.Ordinal);
        if (index < 0 || index % 4 != 0 || index == Alphabet.Length - 1)
        {
            throw new InvalidOperationException("A 32-byte value must end in canonical base64 data with two zero pad bits.");
        }

        return value[..^1] + Alphabet[index + 1];
    }

    private static string EncodeBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DecodeBase64Url(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - normalized.Length % 4) % 4), '=');
        return Convert.FromBase64String(normalized);
    }

    private sealed record TestState(string Value);

    private sealed record IncompatibleState(int Value);
}
