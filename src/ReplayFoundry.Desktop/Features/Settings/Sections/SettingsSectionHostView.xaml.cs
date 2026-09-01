using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ReplayFoundry.Desktop.Features.Settings;

namespace ReplayFoundry.Desktop.Features.Settings.Sections;

public partial class SettingsSectionHostView : UserControl
{
    private readonly IReadOnlyDictionary<SettingsSection, FrameworkElement> _sectionViews;

    public SettingsSectionHostView()
    {
        InitializeComponent();
        _sectionViews = new Dictionary<SettingsSection, FrameworkElement>
        {
            [SettingsSection.Storage] = new StorageSettingsView(),
            [SettingsSection.CreatorVoice] = new CreatorVoiceSettingsView(),
            [SettingsSection.AiModels] = new AiModelsSettingsView(),
            [SettingsSection.PrivacyDiagnostics] = new PrivacyDiagnosticsSettingsView(),
            [SettingsSection.About] = new AboutSettingsView(),
        };
        DataContextChanged += OnDataContextChanged;
        ApplySelectedSection();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is SettingsViewModel previous)
            previous.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is SettingsViewModel current)
            current.PropertyChanged += OnViewModelPropertyChanged;
        ApplySelectedSection();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(SettingsViewModel.SelectedSection))
            ApplySelectedSection();
    }

    private void ApplySelectedSection()
    {
        var viewModel = DataContext as SettingsViewModel;
        var selected = viewModel?.SelectedSection ?? SettingsSection.Storage;
        var view = _sectionViews[selected];
        view.DataContext = viewModel;
        SectionContent.Content = view;
    }
}
