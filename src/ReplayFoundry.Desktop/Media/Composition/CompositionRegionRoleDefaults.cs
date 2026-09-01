using System;

namespace ReplayFoundry.Desktop.Media.Composition;

public static class CompositionRegionRoleDefaults
{
    public static CompositionRegionTraits GetTraits(
        CompositionRegionRole role)
    {
        return role switch
        {
            CompositionRegionRole.Gameplay or
            CompositionRegionRole.Presenter =>
                CompositionRegionTraits.Dynamic,

            CompositionRegionRole.ChatOrText =>
                CompositionRegionTraits.Dynamic |
                CompositionRegionTraits.Occluding,

            CompositionRegionRole.Overlay =>
                CompositionRegionTraits.Static |
                CompositionRegionTraits.Occluding,

            CompositionRegionRole.Unknown =>
                CompositionRegionTraits.None,

            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(role),
                    role,
                    "The composition role is not defined."),
        };
    }
}
