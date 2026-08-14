using ReplayFoundry.Desktop.Features.Generate.CompositionReview;
using ReplayFoundry.Desktop.Features.Generate.Evidence;
using ReplayFoundry.Desktop.Features.Generate.GenerationSetup;
using ReplayFoundry.Desktop.Features.Generate.Preparation;

namespace ReplayFoundry.Desktop.Features.Generate.Workflow;

[Flags]
internal enum GenerationWorkflowSessionChange
{
    None = 0,
    Preparation = 1,
    Setup = 2,
    Composition = 4,
    Evidence = 8,
    All = Preparation | Setup | Composition | Evidence,
}

internal sealed class GenerationWorkflowSessionState
{
    private readonly IGenerationSourcePreparationCoordinator
        _preparationCoordinator;
    private readonly IGenerationEvidenceAnalysisCoordinator
        _evidenceCoordinator;

    private GenerationSetupOptions? _setup;
    private GenerationCompositionReviewResult? _composition;

    public GenerationWorkflowSessionState(
        IGenerationSourcePreparationCoordinator preparationCoordinator,
        IGenerationEvidenceAnalysisCoordinator evidenceCoordinator)
    {
        ArgumentNullException.ThrowIfNull(preparationCoordinator);
        ArgumentNullException.ThrowIfNull(evidenceCoordinator);

        _preparationCoordinator = preparationCoordinator;
        _evidenceCoordinator = evidenceCoordinator;
    }

    public event EventHandler<GenerationWorkflowSessionChangedEventArgs>?
        Changed;

    public GenerationSourcePreparationResult? Preparation =>
        _preparationCoordinator.Current;

    public GenerationSetupOptions? Setup => _setup;

    public GenerationCompositionReviewResult? Composition => _composition;

    public GenerationEvidenceAnalysisResult? Evidence =>
        _evidenceCoordinator.Current;

    public void AcceptPreparation(
        GenerationSourcePreparationResult preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);

        if (!ReferenceEquals(
                _preparationCoordinator.Current,
                preparation))
        {
            throw new ArgumentException(
                "The accepted preparation must be current in the preparation coordinator.",
                nameof(preparation));
        }

        GenerationWorkflowSessionChange change =
            GenerationWorkflowSessionChange.Preparation;

        if (_composition is not null &&
            !ReferenceEquals(
                _composition.Preparation,
                preparation))
        {
            _composition = null;
            _evidenceCoordinator.Invalidate();
            change |=
                GenerationWorkflowSessionChange.Composition |
                GenerationWorkflowSessionChange.Evidence;
        }

        RaiseChanged(change);
    }

    public void SetSetup(
        GenerationSetupOptions setup)
    {
        ArgumentNullException.ThrowIfNull(setup);

        _setup = setup;
        RaiseChanged(GenerationWorkflowSessionChange.Setup);
    }

    public void SetComposition(
        GenerationCompositionReviewResult composition)
    {
        ArgumentNullException.ThrowIfNull(composition);

        if (!ReferenceEquals(
                composition.Preparation,
                Preparation))
        {
            throw new ArgumentException(
                "The composition review does not belong to the current source preparation.",
                nameof(composition));
        }

        _composition = composition;
        RaiseChanged(GenerationWorkflowSessionChange.Composition);
    }

    public void AcceptEvidence(
        GenerationEvidenceAnalysisResult evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        if (!ReferenceEquals(
                _evidenceCoordinator.Current,
                evidence))
        {
            throw new ArgumentException(
                "The accepted evidence must be current in the evidence coordinator.",
                nameof(evidence));
        }

        if (!ReferenceEquals(
                evidence.Request.Preparation,
                Preparation) ||
            !ReferenceEquals(
                evidence.Request.CompositionReview,
                _composition))
        {
            throw new ArgumentException(
                "The evidence must be rebound to the current preparation and composition.",
                nameof(evidence));
        }

        RaiseChanged(GenerationWorkflowSessionChange.Evidence);
    }

    public void InvalidateAfterSourceChange()
    {
        _preparationCoordinator.Invalidate();
        _evidenceCoordinator.Invalidate();
        _setup = null;
        _composition = null;

        RaiseChanged(GenerationWorkflowSessionChange.All);
    }

    public void InvalidateAfterStaleSource()
    {
        InvalidateAfterSourceChange();
    }

    public void InvalidateAfterModeChange()
    {
        if (_setup is null)
        {
            return;
        }

        _setup = null;
        RaiseChanged(GenerationWorkflowSessionChange.Setup);
    }

    public void InvalidateCompositionAndEvidence()
    {
        GenerationWorkflowSessionChange change =
            GenerationWorkflowSessionChange.Evidence;

        if (_composition is not null)
        {
            _composition = null;
            change |= GenerationWorkflowSessionChange.Composition;
        }

        _evidenceCoordinator.Invalidate();
        RaiseChanged(change);
    }

    public void InvalidateEvidence()
    {
        _evidenceCoordinator.Invalidate();
        RaiseChanged(GenerationWorkflowSessionChange.Evidence);
    }

    public void InvalidateAfterAnalyzerOrPolicyChange()
    {
        InvalidateEvidence();
    }

    private void RaiseChanged(
        GenerationWorkflowSessionChange change)
    {
        if (change == GenerationWorkflowSessionChange.None)
        {
            return;
        }

        Changed?.Invoke(
            this,
            new GenerationWorkflowSessionChangedEventArgs(change));
    }
}

internal sealed class GenerationWorkflowSessionChangedEventArgs :
    EventArgs
{
    public GenerationWorkflowSessionChangedEventArgs(
        GenerationWorkflowSessionChange change)
    {
        if (change == GenerationWorkflowSessionChange.None ||
            (change & ~GenerationWorkflowSessionChange.All) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(change),
                change,
                "The workflow-session change is not defined.");
        }

        Change = change;
    }

    public GenerationWorkflowSessionChange Change { get; }

    public bool Includes(
        GenerationWorkflowSessionChange change)
    {
        return (Change & change) == change;
    }
}
