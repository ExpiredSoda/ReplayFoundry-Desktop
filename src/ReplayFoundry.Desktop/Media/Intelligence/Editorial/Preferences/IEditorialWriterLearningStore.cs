namespace ReplayFoundry.Desktop.Media.Intelligence.Editorial.Preferences;

/// <summary>Wording labels belong to their exact factual prompt, not clip taste.</summary>
public interface IEditorialWriterLearningStore
{
    bool IsEnabled { get; }
    string? LearningDirectory => null;
    void RetainValidatedBatch(string contextPath, string validatedOutput,
        IReadOnlyList<ClipEditorialMetadataRequest> requests);
    bool Record(ClipEditorialContext context, string beforeTitle, string beforeDescription,
        IReadOnlyList<string> beforeTags, string afterTitle, string afterDescription,
        IReadOnlyList<string> afterTags, bool explicitApproval = false);
}
