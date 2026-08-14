using System.Windows;
using System.Windows.Controls;

namespace ReplayFoundry.Desktop.Presentation.Controls;

public partial class EmptyState : UserControl
{
    public static readonly DependencyProperty IconTextProperty = DependencyProperty.Register(
        nameof(IconText), typeof(string), typeof(EmptyState), new PropertyMetadata("·"));

    public static readonly DependencyProperty IconKeyProperty = DependencyProperty.Register(
        nameof(IconKey), typeof(string), typeof(EmptyState), new PropertyMetadata("Icon.Info"));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));

    public EmptyState()
    {
        InitializeComponent();
    }

    public string IconText { get => (string)GetValue(IconTextProperty); set => SetValue(IconTextProperty, value); }
    public string IconKey { get => (string)GetValue(IconKeyProperty); set => SetValue(IconKeyProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
}
