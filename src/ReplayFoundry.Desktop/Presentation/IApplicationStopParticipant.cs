namespace ReplayFoundry.Desktop.Presentation;

internal interface IApplicationStopParticipant
{
    Task StopAsync(CancellationToken cancellationToken);
}
