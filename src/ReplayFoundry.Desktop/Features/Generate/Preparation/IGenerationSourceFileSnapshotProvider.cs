namespace ReplayFoundry.Desktop.Features.Generate.Preparation;

public interface IGenerationSourceFileSnapshotProvider
{
    GenerationSourceFileSnapshot Capture(
        string sourcePath);
}
