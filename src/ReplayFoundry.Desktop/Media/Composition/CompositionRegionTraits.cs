using System;

namespace ReplayFoundry.Desktop.Media.Composition;

/// <summary>
/// Describes visual behavior independently from semantic role.
/// </summary>
[Flags]
public enum CompositionRegionTraits
{
    None = 0,
    Static = 1 << 0,
    Dynamic = 1 << 1,
    Transient = 1 << 2,
    Occluding = 1 << 3,
}
