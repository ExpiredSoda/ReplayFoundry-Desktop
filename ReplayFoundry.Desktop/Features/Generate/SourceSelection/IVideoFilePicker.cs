using System.Collections.Generic;

namespace ReplayFoundry.Desktop.Features.Generate.SourceSelection;

public interface IVideoFilePicker
{
    IReadOnlyList<string> PickSingleVideo();

    IReadOnlyList<string> PickMultipleVideos();
}
