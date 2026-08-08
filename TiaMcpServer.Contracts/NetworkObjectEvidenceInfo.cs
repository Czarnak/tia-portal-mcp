using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Evidence about a network object, captured at read time so callers can correlate the result
/// with the selector that produced it without re-reading the hardware configuration.
/// </summary>
public sealed class NetworkObjectEvidenceInfo
{
    public string? Name { get; set; }
    public string? TypeIdentifier { get; set; }
    public int? PositionNumber { get; set; }
    public string? Address { get; set; }
    public List<string> DeviceItemPath { get; set; } = new List<string>();
    public string? InterfaceName { get; set; }
    public string? InterfaceType { get; set; }
    public string? InterfaceOperatingMode { get; set; }
    public string? NodeName { get; set; }
    public string? NodeType { get; set; }
    public string? SubnetName { get; set; }
    public string? NetworkType { get; set; }
    public string? IoSystemName { get; set; }
    public string? IoControllerName { get; set; }
    public bool? ConnectionIsValid { get; set; }
    public string? LocalEndpointName { get; set; }
    public string? PartnerEndpointName { get; set; }
    public string? LocalSubnetName { get; set; }
    public string? PartnerSubnetName { get; set; }
}
