using System;
using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Lightweight address record used for pure channel-to-address interval containment and controller resolution.
/// </summary>
public sealed class IoAddressRecord
{
    public string? IoType { get; set; }

    public int? StartAddress { get; set; }

    public int? Length { get; set; }

    public IReadOnlyList<string> ControllerNames { get; set; } = Array.Empty<string>();

    public bool ControllerAssociationReadable { get; set; } = true;
}

/// <summary>
/// Status of resolving controller ownership for a single channel.
/// </summary>
public enum IoChannelControllerStatus
{
    NoInterval,
    NoContainingAddress,
    MultipleContainingAddresses,
    UnreadableController,
    NoController,
    MultipleControllers,
    Resolved,
}

/// <summary>
/// Outcome of resolving controller ownership for a single channel against its device item's addresses.
/// </summary>
public readonly struct IoChannelControllerResult
{
    public IoChannelControllerStatus Status { get; }

    public string? ControllerName { get; }

    public IoAddressRecord? RelevantAddress { get; }

    public string? DiagnosticMessage { get; }

    public bool IsTargetMatch(string? targetPlcDeviceName)
        => Status == IoChannelControllerStatus.Resolved
           && targetPlcDeviceName is not null
           && string.Equals(ControllerName, targetPlcDeviceName, StringComparison.Ordinal);

    public IoChannelControllerResult(
        IoChannelControllerStatus status,
        string? controllerName,
        IoAddressRecord? relevantAddress,
        string? diagnosticMessage)
    {
        Status = status;
        ControllerName = controllerName;
        RelevantAddress = relevantAddress;
        DiagnosticMessage = diagnosticMessage;
    }
}

/// <summary>
/// Pure, Siemens-free resolver that maps each channel to its containing address record and evaluates
/// controller ownership. Never matches across controllers or falls back to an unrelated address.
/// </summary>
public static class IoChannelControllerResolver
{
    public static IoChannelControllerResult Resolve(
        string? channelIoType,
        int? channelAddressBits,
        uint? channelWidthBits,
        IReadOnlyList<IoAddressRecord>? addresses,
        string itemDescription)
    {
        var channelArea = IoLogicalAddressFormatter.NormalizeArea(channelIoType);
        if (channelArea is null
            || channelAddressBits is null
            || channelWidthBits is null
            || channelAddressBits < 0
            || channelWidthBits == 0)
        {
            return new IoChannelControllerResult(
                IoChannelControllerStatus.NoInterval,
                null,
                null,
                null);
        }

        var channelStartBit = (long)channelAddressBits.Value;
        var channelWidth = (long)channelWidthBits.Value;
        var channelEndBit = channelStartBit + channelWidth;

        if (channelEndBit < channelStartBit)
        {
            return new IoChannelControllerResult(
                IoChannelControllerStatus.NoInterval,
                null,
                null,
                null);
        }

        var relevantAddresses = new List<IoAddressRecord>();
        if (addresses is not null)
        {
            foreach (var address in addresses)
            {
                if (address is null)
                {
                    continue;
                }

                var addressArea = IoLogicalAddressFormatter.NormalizeArea(address.IoType);
                if (addressArea is null || !string.Equals(addressArea, channelArea, StringComparison.Ordinal))
                {
                    continue;
                }

                if (address.StartAddress is null || address.Length is null || address.StartAddress < 0 || address.Length <= 0)
                {
                    continue;
                }

                var addressStartBit = (long)address.StartAddress.Value * 8;
                var addressEndBit = addressStartBit + ((long)address.Length.Value * 8);

                if (addressStartBit <= channelStartBit && channelEndBit <= addressEndBit)
                {
                    relevantAddresses.Add(address);
                }
            }
        }

        if (relevantAddresses.Count == 0)
        {
            return new IoChannelControllerResult(
                IoChannelControllerStatus.NoContainingAddress,
                null,
                null,
                $"No controller association was found for a channel of device item '{itemDescription}'; no tag matches are reported for it.");
        }

        if (relevantAddresses.Count > 1)
        {
            return new IoChannelControllerResult(
                IoChannelControllerStatus.MultipleContainingAddresses,
                null,
                null,
                $"A channel of device item '{itemDescription}' is contained by more than one address; no tag matches are reported because address ownership is ambiguous.");
        }

        var relevant = relevantAddresses[0];
        if (!relevant.ControllerAssociationReadable)
        {
            return new IoChannelControllerResult(
                IoChannelControllerStatus.UnreadableController,
                null,
                relevant,
                $"Controller association was unreadable for a channel of device item '{itemDescription}'; no tag matches are reported for it.");
        }

        var controllerNames = relevant.ControllerNames ?? Array.Empty<string>();
        if (controllerNames.Count == 0)
        {
            return new IoChannelControllerResult(
                IoChannelControllerStatus.NoController,
                null,
                relevant,
                $"No controller association was found for a channel of device item '{itemDescription}'; no tag matches are reported for it.");
        }

        if (controllerNames.Count > 1)
        {
            return new IoChannelControllerResult(
                IoChannelControllerStatus.MultipleControllers,
                null,
                relevant,
                $"A channel of device item '{itemDescription}' is owned by more than one controller; no tag matches are reported for it.");
        }

        return new IoChannelControllerResult(
            IoChannelControllerStatus.Resolved,
            controllerNames[0],
            relevant,
            null);
    }
}
