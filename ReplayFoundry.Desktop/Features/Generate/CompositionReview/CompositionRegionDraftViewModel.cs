using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using ReplayFoundry.Desktop.Media.Composition;
using ReplayFoundry.Desktop.Presentation.Commands;

namespace ReplayFoundry.Desktop.Features.Generate.CompositionReview;

public sealed class CompositionRegionDraftViewModel :
    INotifyPropertyChanged
{
    public const double MinimumSize = 0.02;

    private const CompositionRegionTraits AllDefinedTraits =
        CompositionRegionTraits.Static |
        CompositionRegionTraits.Dynamic |
        CompositionRegionTraits.Transient |
        CompositionRegionTraits.Occluding;

    private readonly Action<CompositionRegionDraftViewModel>
        _changed;

    private readonly Action<CompositionRegionDraftViewModel>
        _selectionRequested;

    private readonly Action<CompositionRegionDraftViewModel>
        _removalRequested;

    private double _x;
    private double _y;
    private double _width;
    private double _height;
    private double _previewWidth = 1280;
    private double _previewHeight = 720;
    private CompositionRegionRole _role;
    private CompositionRegionTraits _traits;
    private bool _isSelected;

    internal CompositionRegionDraftViewModel(
        string id,
        NormalizedRectangle geometry,
        CompositionRegionRole role,
        CompositionRegionTraits traits,
        Action<CompositionRegionDraftViewModel> changed,
        Action<CompositionRegionDraftViewModel> selectionRequested,
        Action<CompositionRegionDraftViewModel> removalRequested)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException(
                "A draft region requires an identifier.",
                nameof(id));
        }

        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(changed);
        ArgumentNullException.ThrowIfNull(selectionRequested);
        ArgumentNullException.ThrowIfNull(removalRequested);

        ValidateRole(role);
        ValidateTraits(traits);

        Id = id.Trim();
        _x = geometry.X;
        _y = geometry.Y;
        _width = geometry.Width;
        _height = geometry.Height;
        _role = role;
        _traits = traits;
        _changed = changed;
        _selectionRequested = selectionRequested;
        _removalRequested = removalRequested;
        RemoveCommand = new DelegateCommand(RequestRemoval);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public string DisplayName =>
        $"{GetRoleDisplayName(Role)} · {Id}";

    public string AutomationName =>
        $"{GetRoleDisplayName(Role)} region {Id}";

    public ICommand RemoveCommand { get; }

    public double X => _x;

    public double Y => _y;

    public double Width => _width;

    public double Height => _height;

    public double PixelX =>
        X * _previewWidth;

    public double PixelY =>
        Y * _previewHeight;

    public double PixelWidth =>
        Width * _previewWidth;

    public double PixelHeight =>
        Height * _previewHeight;

    public CompositionRegionRole Role
    {
        get => _role;

        set
        {
            ValidateRole(value);

            if (_role == value)
            {
                return;
            }

            _role = value;
            _traits = CompositionRegionRoleDefaults.GetTraits(value);

            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(AutomationName));
            RaiseTraitPropertiesChanged();

            _changed(this);
        }
    }

    public CompositionRegionTraits Traits
    {
        get => _traits;

        set
        {
            ValidateTraits(value);

            if (_traits == value)
            {
                return;
            }

            _traits = value;

            RaiseTraitPropertiesChanged();
            _changed(this);
        }
    }

    public bool IsStatic
    {
        get =>
            Traits.HasFlag(
                CompositionRegionTraits.Static);

        set =>
            SetTrait(
                CompositionRegionTraits.Static,
                value,
                CompositionRegionTraits.Dynamic);
    }

    public bool IsDynamic
    {
        get =>
            Traits.HasFlag(
                CompositionRegionTraits.Dynamic);

        set =>
            SetTrait(
                CompositionRegionTraits.Dynamic,
                value,
                CompositionRegionTraits.Static);
    }

    public bool IsTransient
    {
        get =>
            Traits.HasFlag(
                CompositionRegionTraits.Transient);

        set =>
            SetTrait(
                CompositionRegionTraits.Transient,
                value);
    }

    public bool IsOccluding
    {
        get =>
            Traits.HasFlag(
                CompositionRegionTraits.Occluding);

        set =>
            SetTrait(
                CompositionRegionTraits.Occluding,
                value);
    }

    public bool IsSelected
    {
        get => _isSelected;

        internal set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public void RequestSelection()
    {
        _selectionRequested(this);
    }

    public void RequestRemoval()
    {
        _removalRequested(this);
    }

    public void SetGeometry(
        double x,
        double y,
        double width,
        double height)
    {
        var geometry =
            new NormalizedRectangle(
                x,
                y,
                width,
                height);

        ApplyGeometry(
            geometry.X,
            geometry.Y,
            geometry.Width,
            geometry.Height);
    }

    public void MoveBy(
        double horizontalDelta,
        double verticalDelta)
    {
        ApplyGeometry(
            CompositionRegionGeometryEditor.Move(
                CreateGeometry(),
                horizontalDelta,
                verticalDelta));
    }

    public void ResizeBy(
        double widthDelta,
        double heightDelta)
    {
        ResizeFromHandle(
            CompositionRegionResizeHandle.BottomRight,
            widthDelta,
            heightDelta);
    }

    public void ResizeFromHandle(
        CompositionRegionResizeHandle handle,
        double horizontalDelta,
        double verticalDelta)
    {
        ApplyGeometry(
            CompositionRegionGeometryEditor.Resize(
                CreateGeometry(),
                handle,
                horizontalDelta,
                verticalDelta,
                MinimumSize));
    }

    internal CompositionRegion CreateRegion()
    {
        CompositionValueSource roleSource =
            Role == CompositionRegionRole.Unknown
                ? CompositionValueSource.NotAvailable
                : CompositionValueSource.UserConfirmed;

        CompositionConfidence roleConfidence =
            Role == CompositionRegionRole.Unknown
                ? CompositionConfidence.None
                : CompositionConfidence.Certain;

        return new CompositionRegion(
            Id,
            CreateGeometry(),
            Role,
            Traits,
            geometryConfidence:
                CompositionConfidence.Certain,
            roleConfidence:
                roleConfidence,
            geometrySource:
                CompositionValueSource.UserConfirmed,
            roleSource:
                roleSource);
    }

    internal void SetPreviewDimensions(
        int width,
        int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height));
        }

        if (_previewWidth == width &&
            _previewHeight == height)
        {
            return;
        }

        _previewWidth = width;
        _previewHeight = height;

        RaisePixelGeometryChanged();
    }

    private static string GetRoleDisplayName(
        CompositionRegionRole role)
    {
        return role switch
        {
            CompositionRegionRole.Gameplay =>
                "Gameplay",
            CompositionRegionRole.Presenter =>
                "Presenter",
            CompositionRegionRole.ChatOrText =>
                "Chat/Text",
            CompositionRegionRole.Overlay =>
                "Overlay",
            CompositionRegionRole.Unknown =>
                "Unknown",
            _ =>
                throw new ArgumentOutOfRangeException(
                    nameof(role)),
        };
    }

    private void SetTrait(
        CompositionRegionTraits trait,
        bool value,
        CompositionRegionTraits mutuallyExclusive =
            CompositionRegionTraits.None)
    {
        CompositionRegionTraits updated =
            value
                ? Traits | trait
                : Traits & ~trait;

        if (value &&
            mutuallyExclusive !=
            CompositionRegionTraits.None)
        {
            updated &=
                ~mutuallyExclusive;
        }

        Traits = updated;
    }

    private void ApplyGeometry(
        NormalizedRectangle geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ApplyGeometry(
            geometry.X,
            geometry.Y,
            geometry.Width,
            geometry.Height);
    }

    private NormalizedRectangle CreateGeometry() =>
        new(X, Y, Width, Height);

    private void ApplyGeometry(
        double x,
        double y,
        double width,
        double height)
    {
        if (_x == x &&
            _y == y &&
            _width == width &&
            _height == height)
        {
            return;
        }

        _x = x;
        _y = y;
        _width = width;
        _height = height;

        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        RaisePixelGeometryChanged();

        _changed(this);
    }

    private void RaisePixelGeometryChanged()
    {
        OnPropertyChanged(nameof(PixelX));
        OnPropertyChanged(nameof(PixelY));
        OnPropertyChanged(nameof(PixelWidth));
        OnPropertyChanged(nameof(PixelHeight));
    }

    private void RaiseTraitPropertiesChanged()
    {
        OnPropertyChanged(nameof(Traits));
        OnPropertyChanged(nameof(IsStatic));
        OnPropertyChanged(nameof(IsDynamic));
        OnPropertyChanged(nameof(IsTransient));
        OnPropertyChanged(nameof(IsOccluding));
    }

    private static void ValidateRole(
        CompositionRegionRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(
                nameof(role));
        }
    }

    private static void ValidateTraits(
        CompositionRegionTraits traits)
    {
        if ((traits & ~AllDefinedTraits) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(traits),
                traits,
                "The draft contains undefined behavior traits.");
        }

        if (traits.HasFlag(
                CompositionRegionTraits.Static) &&
            traits.HasFlag(
                CompositionRegionTraits.Dynamic))
        {
            throw new ArgumentException(
                "A draft region cannot be both Static and Dynamic.",
                nameof(traits));
        }
    }

    private void OnPropertyChanged(
        [CallerMemberName]
        string? propertyName = null)
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(
                propertyName));
    }
}
