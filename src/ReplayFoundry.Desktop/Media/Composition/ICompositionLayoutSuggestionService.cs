using ReplayFoundry.Desktop.Media.Preview;

namespace ReplayFoundry.Desktop.Media.Composition;

public sealed record CompositionLayoutSuggestion(
    NormalizedRectangle Presenter,
    NormalizedRectangle? Gameplay,
    string Description);

public interface ICompositionLayoutSuggestionService
{
    Task<CompositionLayoutSuggestion?> SuggestAsync(VideoPreviewFrame frame, CancellationToken cancellationToken);
}

public static class CompositionLayoutSuggestionPolicy
{
    // These are editable framing hints, not a semantic claim that a face belongs to the creator.
    public static CompositionLayoutSuggestion? FromFaces(
        IReadOnlyList<NormalizedRectangle> faces, bool portrait)
    {
        if (faces.Count != 1) return null;
        NormalizedRectangle face = faces[0];
        double centerY = face.Y + face.Height / 2;
        if (portrait && centerY < 0.22)
        {
            double split = Math.Clamp(face.Y + face.Height * 1.8, 0.18, 0.4);
            return new(new(0, 0, 1, split), new(0, split, 1, 1 - split),
                "Possible camera at the top, with gameplay below. Check the boxes after adding them.");
        }
        if (portrait && centerY > 0.78)
        {
            double split = Math.Clamp(face.Y - face.Height * 0.8, 0.6, 0.82);
            return new(new(0, split, 1, 1 - split), new(0, 0, 1, split),
                "Possible camera at the bottom, with gameplay above. Check the boxes after adding them.");
        }
        double left = Math.Max(0, face.X - face.Width * 0.7);
        double top = Math.Max(0, face.Y - face.Height * 0.6);
        double right = Math.Min(1, face.X + face.Width * 1.7);
        double bottom = Math.Min(1, face.Y + face.Height * 1.9);
        return new(new(left, top, right - left, bottom - top), null,
            "Found a possible camera face. Add its box, then adjust it to include the whole camera view.");
    }
}
