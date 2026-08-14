using System.Collections.ObjectModel;

namespace ReplayFoundry.Desktop.Features.Generate.SourceSelection;

internal sealed class GenerationSourceSelectionState
{
    private readonly VideoSourceValidator _validator;
    private readonly ObservableCollection<SelectedVideoSource> _sources = [];
    private readonly HashSet<string> _paths =
        new(StringComparer.OrdinalIgnoreCase);

    public GenerationSourceSelectionState(
        VideoSourceValidator validator)
    {
        ArgumentNullException.ThrowIfNull(validator);

        _validator = validator;
        Sources = new ReadOnlyObservableCollection<SelectedVideoSource>(
            _sources);
    }

    public event EventHandler<GenerationSourceSelectionChangedEventArgs>?
        Changed;

    public ReadOnlyObservableCollection<SelectedVideoSource> Sources { get; }

    public int Count => _sources.Count;

    public bool HasSources => Count > 0;

    public string? ValidationMessage { get; private set; }

    public bool AddCandidates(
        IEnumerable<string> candidatePaths)
    {
        ArgumentNullException.ThrowIfNull(candidatePaths);

        var validationMessages = new List<string>();
        int duplicateCount = 0;
        int addedCount = 0;
        bool receivedCandidate = false;

        foreach (string candidatePath in candidatePaths)
        {
            receivedCandidate = true;

            if (!_validator.TryValidate(
                    candidatePath,
                    out string normalizedPath,
                    out string errorMessage))
            {
                validationMessages.Add(errorMessage);
                continue;
            }

            if (!_paths.Add(normalizedPath))
            {
                duplicateCount++;
                continue;
            }

            _sources.Add(
                new SelectedVideoSource(
                    normalizedPath,
                    isReference: _sources.Count == 0));

            addedCount++;
        }

        if (!receivedCandidate)
        {
            return false;
        }

        if (duplicateCount > 0)
        {
            validationMessages.Add(
                duplicateCount == 1
                    ? "This file has already been added."
                    : $"{duplicateCount} duplicate files were already selected.");
        }

        bool validationChanged = SetValidationMessage(
            validationMessages.Count == 0
                ? null
                : string.Join(
                    Environment.NewLine,
                    validationMessages));

        bool sourcesChanged = addedCount > 0;
        RaiseChanged(sourcesChanged, validationChanged);

        return sourcesChanged;
    }

    public bool Remove(
        string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            throw new ArgumentException(
                "A selected source path is required.",
                nameof(fullPath));
        }

        int index = -1;

        for (int candidateIndex = 0;
             candidateIndex < _sources.Count;
             candidateIndex++)
        {
            if (string.Equals(
                    _sources[candidateIndex].FullPath,
                    fullPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                index = candidateIndex;
                break;
            }
        }

        if (index < 0)
        {
            return false;
        }

        bool removedReference = _sources[index].IsReference;
        _paths.Remove(_sources[index].FullPath);
        _sources.RemoveAt(index);

        if (removedReference && _sources.Count > 0)
        {
            RebuildReference(_sources[0].FullPath);
        }

        RaiseChanged(sourcesChanged: true, validationChanged: false);
        return true;
    }

    public bool SetReference(
        string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            throw new ArgumentException(
                "A reference source path is required.",
                nameof(fullPath));
        }

        SelectedVideoSource? reference = _sources.FirstOrDefault(
            source => string.Equals(
                source.FullPath,
                fullPath,
                StringComparison.OrdinalIgnoreCase));

        if (reference is null)
        {
            throw new ArgumentException(
                "The reference source must belong to the selected sources.",
                nameof(fullPath));
        }

        if (reference.IsReference)
        {
            return false;
        }

        RebuildReference(reference.FullPath);
        RaiseChanged(sourcesChanged: true, validationChanged: false);
        return true;
    }

    public bool Clear()
    {
        if (_sources.Count == 0)
        {
            return false;
        }

        _sources.Clear();
        _paths.Clear();
        bool validationChanged = SetValidationMessage(null);

        RaiseChanged(sourcesChanged: true, validationChanged);
        return true;
    }

    public IReadOnlyList<SelectedVideoSource> CreateSnapshot()
    {
        return Array.AsReadOnly(
            _sources.ToArray());
    }

    public IReadOnlyList<string> ValidateCurrentSelection()
    {
        var errors = new List<string>();

        foreach (SelectedVideoSource source in _sources)
        {
            if (!_validator.TryValidate(
                    source.FullPath,
                    out string normalizedPath,
                    out string errorMessage))
            {
                errors.Add(errorMessage);
                continue;
            }

            if (!string.Equals(
                    source.FullPath,
                    normalizedPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(
                    $"The selected file path changed unexpectedly: '{source.FullPath}'.");
            }
        }

        return errors.AsReadOnly();
    }

    public void ReportValidation(
        string? message)
    {
        bool changed = SetValidationMessage(message);
        RaiseChanged(sourcesChanged: false, validationChanged: changed);
    }

    private void RebuildReference(
        string referencePath)
    {
        for (int index = 0; index < _sources.Count; index++)
        {
            SelectedVideoSource source = _sources[index];
            bool isReference = string.Equals(
                source.FullPath,
                referencePath,
                StringComparison.OrdinalIgnoreCase);

            if (source.IsReference != isReference)
            {
                _sources[index] =
                    new SelectedVideoSource(
                        source.FullPath,
                        isReference);
            }
        }
    }

    private bool SetValidationMessage(
        string? message)
    {
        if (string.Equals(
                ValidationMessage,
                message,
                StringComparison.Ordinal))
        {
            return false;
        }

        ValidationMessage = message;
        return true;
    }

    private void RaiseChanged(
        bool sourcesChanged,
        bool validationChanged)
    {
        if (!sourcesChanged && !validationChanged)
        {
            return;
        }

        Changed?.Invoke(
            this,
            new GenerationSourceSelectionChangedEventArgs(
                sourcesChanged,
                validationChanged));
    }
}

internal sealed class GenerationSourceSelectionChangedEventArgs :
    EventArgs
{
    public GenerationSourceSelectionChangedEventArgs(
        bool sourcesChanged,
        bool validationChanged)
    {
        if (!sourcesChanged && !validationChanged)
        {
            throw new ArgumentException(
                "A source-selection change must identify an affected projection.");
        }

        SourcesChanged = sourcesChanged;
        ValidationChanged = validationChanged;
    }

    public bool SourcesChanged { get; }

    public bool ValidationChanged { get; }
}
