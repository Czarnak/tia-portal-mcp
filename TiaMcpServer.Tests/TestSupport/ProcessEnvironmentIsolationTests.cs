using System.Reflection;
using System.Text.RegularExpressions;
using TiaMcpServer.Tests.Worker;
using Xunit;

namespace TiaMcpServer.Tests.TestSupport;

/// <summary>
/// FakeWorker reads its scenario from process environment variables, and every child worker
/// inherits the testhost's environment. A test class that changes that environment must run in a
/// DisableParallelization collection, which xUnit runs alone after the parallel phase. This scans
/// the test sources for environment changes and follows them through helper (non-test) types,
/// such as FakeWorkerUiOpenProject or FakeWorkerBinding, to every test class that uses them.
/// </summary>
public sealed class ProcessEnvironmentIsolationTests
{
    /// <summary>
    /// Helpers that change the environment only on one path. The harness binds a startup project
    /// through FakeWorkerBinding only when the caller passes the named argument.
    /// </summary>
    // ponytail: matches the named argument only; a positional startup path is not detected.
    private static readonly Dictionary<string, string> ConditionalHelpers = new()
    {
        [nameof(McpProtocolTestHarness)] = "startupProjectPath:",
    };

    [Fact]
    public void ClassesThatChangeProcessEnvironment_RunInSerialCollections()
    {
        var sources = ReadTestSources();
        // This class names the helpers itself, so it is left out of its own scan.
        var types = typeof(ProcessEnvironmentIsolationTests).Assembly.GetTypes()
            .Where(type => !type.IsNested && !type.Name.Contains('<') && type != typeof(ProcessEnvironmentIsolationTests))
            .ToArray();
        var declaringSources = types.ToDictionary(
            type => type,
            type => sources.Where(source => source.Declared.Contains(SimpleName(type))).ToArray());

        var changing = types
            .Where(type => declaringSources[type].Any(source =>
                source.Text.Contains("Environment.SetEnvironmentVariable(", StringComparison.Ordinal)))
            .ToHashSet();
        bool added;
        do
        {
            var helperNames = changing.Where(type => !IsTestClass(type)).Select(SimpleName).ToArray();
            added = false;
            foreach (var type in types.Where(type => !changing.Contains(type)))
            {
                if (declaringSources[type].Any(source => helperNames.Any(source.Uses)))
                    added |= changing.Add(type);
            }
        }
        while (added);

        // Proves the scan found the sources and follows helpers more than one level deep.
        Assert.Contains(typeof(FakeWorkerUiOpenProject), changing);
        Assert.Contains(typeof(FakeWorkerBinding), changing);

        var serialCollections = types
            .Where(type => type.GetCustomAttribute<CollectionDefinitionAttribute>()?.DisableParallelization == true)
            .Select(ConstructorName<CollectionDefinitionAttribute>)
            .ToHashSet();
        var unsafeClasses = changing
            .Where(IsTestClass)
            .Where(type => !serialCollections.Contains(ConstructorName<CollectionAttribute>(type)))
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(unsafeClasses.Length == 0,
            "These test classes change the process environment (directly or through a helper) but run in parallel: "
            + string.Join(", ", unsafeClasses));
    }

    private sealed record Source(string Text, HashSet<string> Declared, HashSet<string> Words)
    {
        public bool Uses(string helper)
            => Words.Contains(helper)
                && (!ConditionalHelpers.TryGetValue(helper, out var argument)
                    || Text.Contains(argument, StringComparison.Ordinal));
    }

    private static Source[] ReadTestSources()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "TiaMcpServer.slnx")))
            root = root.Parent;
        if (root is null)
            throw new DirectoryNotFoundException("Could not locate the repository root.");

        var testsRoot = Path.Combine(root.FullName, "TiaMcpServer.Tests");
        return Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(testsRoot, path).Split(Path.DirectorySeparatorChar)
                .Any(segment => segment is "bin" or "obj"))
            .Select(path => Regex.Replace(File.ReadAllText(path), @"/\*.*?\*/|//[^\n]*", "", RegexOptions.Singleline))
            .Select(text => new Source(
                text,
                Regex.Matches(text, @"\b(?:class|record|struct)\s+(\w+)").Select(match => match.Groups[1].Value).ToHashSet(),
                Regex.Matches(text, @"\w+").Select(match => match.Value).ToHashSet()))
            .ToArray();
    }

    private static string SimpleName(Type type) => type.Name.Split('`')[0];

    private static bool IsTestClass(Type type)
        => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Any(method => method.IsDefined(typeof(FactAttribute), inherit: true));

    private static string? ConstructorName<TAttribute>(Type type) => type.GetCustomAttributesData()
        .FirstOrDefault(attribute => attribute.AttributeType == typeof(TAttribute))?
        .ConstructorArguments.Select(argument => argument.Value as string).FirstOrDefault();
}
