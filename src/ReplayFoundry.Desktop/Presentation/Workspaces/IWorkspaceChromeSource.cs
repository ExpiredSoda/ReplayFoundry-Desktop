using System.ComponentModel;

namespace ReplayFoundry.Desktop.Presentation.Workspaces;

public interface IWorkspaceChromeSource : INotifyPropertyChanged
{
    string WorkspaceEyebrow { get; }

    string WorkspaceTitle { get; }

    string WorkspaceDescription { get; }

    string StatusText { get; }
}
