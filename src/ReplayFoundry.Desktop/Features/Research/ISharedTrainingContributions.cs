namespace ReplayFoundry.Desktop.Features.Research;

public interface ISharedTrainingContributions
{
    bool IsEnabled { get; }
    string Status { get; }
    void Enable();
    void Disable();
    Task SendPendingAsync();
    Task DeleteSharedAsync();
}
