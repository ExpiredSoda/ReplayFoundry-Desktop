using System.Windows;
using System.Windows.Controls;

namespace ReplayFoundry.Desktop.Presentation.Controls;

public partial class IssuePanel : UserControl
{
    public static readonly DependencyProperty ReferenceProperty = DependencyProperty.Register(
        nameof(Reference), typeof(string), typeof(IssuePanel), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty SummaryProperty = DependencyProperty.Register(
        nameof(Summary), typeof(string), typeof(IssuePanel), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty SuggestionProperty = DependencyProperty.Register(
        nameof(Suggestion), typeof(string), typeof(IssuePanel), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty DetailsProperty = DependencyProperty.Register(
        nameof(Details), typeof(string), typeof(IssuePanel), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty IconKeyProperty = DependencyProperty.Register(
        nameof(IconKey), typeof(string), typeof(IssuePanel), new PropertyMetadata("Icon.Info"));
    public static readonly DependencyProperty StageOneProperty = DependencyProperty.Register(
        nameof(StageOne), typeof(string), typeof(IssuePanel), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty StageTwoProperty = DependencyProperty.Register(
        nameof(StageTwo), typeof(string), typeof(IssuePanel), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty StageThreeProperty = DependencyProperty.Register(
        nameof(StageThree), typeof(string), typeof(IssuePanel), new PropertyMetadata(string.Empty));

    public IssuePanel() => InitializeComponent();

    public string Reference { get => (string)GetValue(ReferenceProperty); set => SetValue(ReferenceProperty, value); }
    public string Summary { get => (string)GetValue(SummaryProperty); set => SetValue(SummaryProperty, value); }
    public string Suggestion { get => (string)GetValue(SuggestionProperty); set => SetValue(SuggestionProperty, value); }
    public string Details { get => (string)GetValue(DetailsProperty); set => SetValue(DetailsProperty, value); }
    public string IconKey { get => (string)GetValue(IconKeyProperty); set => SetValue(IconKeyProperty, value); }
    public string StageOne { get => (string)GetValue(StageOneProperty); set => SetValue(StageOneProperty, value); }
    public string StageTwo { get => (string)GetValue(StageTwoProperty); set => SetValue(StageTwoProperty, value); }
    public string StageThree { get => (string)GetValue(StageThreeProperty); set => SetValue(StageThreeProperty, value); }
}
