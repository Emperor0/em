namespace D7.Core.Models;

public enum CapabilityState
{
    Supported,
    ReadOnly,
    Unavailable,
    Experimental
}

public sealed record Capability(
    string Id,
    string NameAr,
    CapabilityState State,
    string ReasonAr,
    string? TechnicalDetail = null);
