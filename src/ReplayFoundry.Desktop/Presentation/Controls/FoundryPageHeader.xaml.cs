using System.Windows;
using System.Windows.Controls;

namespace ReplayFoundry.Desktop.Presentation.Controls;

public partial class FoundryPageHeader : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(FoundryPageHeader), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(FoundryPageHeader), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty IconKeyProperty = DependencyProperty.Register(
        nameof(IconKey), typeof(string), typeof(FoundryPageHeader), new PropertyMetadata("Icon.Project"));

    public FoundryPageHeader() => InitializeComponent();

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string IconKey { get => (string)GetValue(IconKeyProperty); set => SetValue(IconKeyProperty, value); }
}
