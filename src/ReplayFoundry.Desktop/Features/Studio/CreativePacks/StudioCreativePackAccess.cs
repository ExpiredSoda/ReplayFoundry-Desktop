using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Features.Studio.CreativePacks;

public enum StudioCreativePackAccessCode
{
    BuiltIn,
    Free,
    Purchased,
    NotOwned,
    Unavailable,
}

public sealed class StudioCreativePackAccessResult
{
    private readonly ReadOnlyCollection<string> _warnings;

    public StudioCreativePackAccessResult(
        string packId,
        StudioCreativePackAccessCode code,
        string provenance,
        IEnumerable<string>? warnings = null)
    {
        PackId = CreativePackValidation.Identifier(packId, nameof(packId));
        if (!Enum.IsDefined(code))
        {
            throw new ArgumentOutOfRangeException(nameof(code));
        }

        Code = code;
        Provenance = CreativePackValidation.Required(
            provenance,
            nameof(provenance));
        string[] snapshot = (warnings ?? []).Select(
            static warning => CreativePackValidation.Required(
                warning,
                nameof(warnings))).ToArray();
        _warnings = Array.AsReadOnly(snapshot);
    }

    public string PackId { get; }

    public StudioCreativePackAccessCode Code { get; }

    public bool CanUsePack =>
        Code is StudioCreativePackAccessCode.BuiltIn or
            StudioCreativePackAccessCode.Free or
            StudioCreativePackAccessCode.Purchased;

    public string Provenance { get; }

    public IReadOnlyList<string> Warnings => _warnings;
}
