using Microsoft.Win32;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Versioning;
using TiaMcpServer.Diagnostics;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

[SupportedOSPlatform("windows")]
public class ApplicationInfoServiceTests
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    [Fact]
    public void HostVersion_PreservesInformationalSemVerPrerelease()
    {
        var assembly = CreateAssembly(
            "PrereleaseHost",
            new Version(1, 2, 3),
            "1.2.3-rc.1");
        var service = new ApplicationInfoService(
            new FakeRegistryService(),
            () => true,
            () => new Version(10, 0, 22000),
            () => assembly);

        Assert.Equal("1.2.3-rc.1", service.HostVersion);
    }

    [Fact]
    public void HostVersion_WithoutInformationalVersion_FallsBackToAssemblyVersion()
    {
        var assembly = CreateAssembly("AssemblyVersionHost", new Version(4, 5, 6, 7));
        var service = new ApplicationInfoService(
            new FakeRegistryService(),
            () => true,
            () => new Version(10, 0, 22000),
            () => assembly);

        Assert.Equal("4.5.6.7", service.HostVersion);
    }

    [Fact]
    public void OsName_Windows10ProductNameAtBuild22000_ReportsWindows11()
    {
        var registry = CreateRegistryWithProductName("Windows 10 Pro");
        var service = new ApplicationInfoService(registry, () => true, () => new Version(10, 0, 22000));

        Assert.Equal("Windows 11 Pro", service.OsName);
    }

    [Fact]
    public void OsName_Windows10ProductNameBelowBuild22000_RemainsWindows10()
    {
        var registry = CreateRegistryWithProductName("Windows 10 Pro");
        var service = new ApplicationInfoService(registry, () => true, () => new Version(10, 0, 21999));

        Assert.Equal("Windows 10 Pro", service.OsName);
    }

    [Fact]
    public void OsName_ServerProductNameAtBuild22000_IsPreserved()
    {
        var registry = CreateRegistryWithProductName("Windows Server 2022 Standard");
        var service = new ApplicationInfoService(registry, () => true, () => new Version(10, 0, 22000));

        Assert.Equal("Windows Server 2022 Standard", service.OsName);
    }

    private static FakeRegistryService CreateRegistryWithProductName(string productName)
    {
        var registry = new FakeRegistryService();
        registry.SetStringValue(
            RegistryHive.LocalMachine,
            RegistryView.Registry64,
            CurrentVersionKey,
            "ProductName",
            productName);
        return registry;
    }

    private static Assembly CreateAssembly(
        string name,
        Version assemblyVersion,
        string? informationalVersion = null)
    {
        var assemblyName = new AssemblyName(name) { Version = assemblyVersion };
        var assembly = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        if (informationalVersion is not null)
        {
            var constructor = typeof(AssemblyInformationalVersionAttribute).GetConstructor([typeof(string)])!;
            assembly.SetCustomAttribute(new CustomAttributeBuilder(constructor, [informationalVersion]));
        }

        return assembly;
    }
}
