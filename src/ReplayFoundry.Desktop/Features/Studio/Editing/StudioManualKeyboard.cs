using System.Windows.Input;

namespace ReplayFoundry.Desktop.Features.Studio.Editing;

internal static class StudioManualKeyboard
{
    internal static bool Handle(StudioManualClipViewModel model, Key key, ModifierKeys modifiers)
    {
        bool shift = modifiers.HasFlag(ModifierKeys.Shift);
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            if (modifiers.HasFlag(ModifierKeys.Alt) || modifiers.HasFlag(ModifierKeys.Windows)) return false;
            ICommand? command = key switch
            {
                Key.Z => shift ? model.RedoRangeCommand : model.UndoRangeCommand,
                Key.Y => model.RedoRangeCommand,
                _ => null,
            };
            if (command is null) return false;
            if (command.CanExecute(null)) command.Execute(null);
            return true;
        }
        if (modifiers.HasFlag(ModifierKeys.Windows)) return false;
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            if (key is not (Key.Left or Key.Right)) return false;
            model.MoveRange(model.SelectionStartSeconds + (key == Key.Left ? -1 : 1) * (shift ? 10 : 1) * model.FrameStepSeconds);
            return true;
        }
        switch (key)
        {
            case Key.I: model.MarkStartCommand.Execute(null); break;
            case Key.O: model.MarkEndCommand.Execute(null); break;
            case Key.Space:
                if (model.Preview.PlayCommand.CanExecute(null)) model.Preview.PlayCommand.Execute(null);
                break;
            case Key.Left: model.StepFrames(shift ? -10 : -1); break;
            case Key.Right: model.StepFrames(shift ? 10 : 1); break;
            case Key.Home: model.SourcePositionSeconds = 0; break;
            case Key.End: model.SourcePositionSeconds = model.SourceMaximumSeconds; break;
            case Key.Up: model.SourcePositionSeconds = model.SelectionStartSeconds; break;
            case Key.Down: model.SourcePositionSeconds = model.SelectionEndSeconds; break;
            case Key.OemPlus: case Key.Add: model.ZoomInCommand.Execute(null); break;
            case Key.OemMinus: case Key.Subtract: model.ZoomOutCommand.Execute(null); break;
            case Key.F: model.ShowSelectionCommand.Execute(null); break;
            case Key.Z when shift: model.ShowAllCommand.Execute(null); break;
            case Key.X: model.PreviewSelectionCommand.Execute(null); break;
            default: return false;
        }
        return true;
    }
}
