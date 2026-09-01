using System.IO;
using ReplayFoundry.Desktop.Features.Generate.Preparation;
using ReplayFoundry.Desktop.Features.Generate.SourceSelection;

namespace ReplayFoundry.Desktop.Features.Generate.Workflow;

internal sealed record GenerationSourceValidityFailure(
    string FriendlyMessage,
    Exception Exception);

internal sealed class GenerationSourceValidityCoordinator
{
    private readonly GenerationSourceSelectionState _sourceSelection;
    private readonly IGenerationSourcePreparationCoordinator
        _preparationCoordinator;
    private readonly GenerationWorkflowSessionState _session;

    public GenerationSourceValidityCoordinator(
        GenerationSourceSelectionState sourceSelection,
        IGenerationSourcePreparationCoordinator preparationCoordinator,
        GenerationWorkflowSessionState session)
    {
        _sourceSelection = sourceSelection ??
            throw new ArgumentNullException(nameof(sourceSelection));
        _preparationCoordinator = preparationCoordinator ??
            throw new ArgumentNullException(nameof(preparationCoordinator));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public bool RevalidateSelection()
    {
        IReadOnlyList<string> errors =
            _sourceSelection.ValidateCurrentSelection();
        _sourceSelection.ReportValidation(
            errors.Count == 0
                ? null
                : string.Join(Environment.NewLine, errors));
        return errors.Count == 0;
    }

    public GenerationSourceValidityFailure? ValidateAfterDialog(
        string friendlyPrefix)
    {
        IReadOnlyList<string> errors =
            _sourceSelection.ValidateCurrentSelection();
        if (errors.Count == 0)
        {
            return null;
        }

        string message = string.Join(Environment.NewLine, errors);
        _sourceSelection.ReportValidation(message);
        _session.InvalidateAfterStaleSource();
        return new GenerationSourceValidityFailure(
            friendlyPrefix + " " + message,
            new IOException(message));
    }

    public GenerationSourceValidityFailure? EnsureFresh(
        GenerationSourcePreparationResult preparation,
        string? genericMessage)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        try
        {
            _preparationCoordinator.EnsureFresh(preparation);
            return null;
        }
        catch (Exception exception)
            when (exception is
                GenerationSourcePreparationException or
                InvalidOperationException)
        {
            _session.InvalidateAfterStaleSource();
            return new GenerationSourceValidityFailure(
                exception.Message,
                exception);
        }
        catch (Exception exception)
        {
            _session.InvalidateAfterStaleSource();
            return new GenerationSourceValidityFailure(
                genericMessage ?? exception.Message,
                exception);
        }
    }

    public Exception? CheckFreshness(
        GenerationSourcePreparationResult preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        try
        {
            _preparationCoordinator.EnsureFresh(preparation);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
