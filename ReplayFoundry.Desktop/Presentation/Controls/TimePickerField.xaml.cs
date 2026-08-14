using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ReplayFoundry.Desktop.Presentation.Controls;

public partial class TimePickerField : UserControl
{
    public static readonly DependencyProperty TimeTextProperty =
        DependencyProperty.Register(
            nameof(TimeText),
            typeof(string),
            typeof(TimePickerField),
            new FrameworkPropertyMetadata(
                "6:00 PM",
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnTimeTextChanged));

    public static readonly DependencyProperty AutomationNameProperty =
        DependencyProperty.Register(
            nameof(AutomationName),
            typeof(string),
            typeof(TimePickerField),
            new PropertyMetadata("Choose time"));

    private bool _synchronizing;

    public TimePickerField()
    {
        InitializeComponent();
        HourList.ItemsSource = Enumerable.Range(1, 12).ToArray();
        MinuteList.ItemsSource = Enumerable.Range(0, 60)
            .Select(static value => value.ToString("00", CultureInfo.InvariantCulture))
            .ToArray();
        PeriodList.ItemsSource = new[] { "AM", "PM" };
        SynchronizeSelection(TimeText);
    }

    public string TimeText
    {
        get => (string)GetValue(TimeTextProperty);
        set => SetValue(TimeTextProperty, value);
    }

    public string AutomationName
    {
        get => (string)GetValue(AutomationNameProperty);
        set => SetValue(AutomationNameProperty, value);
    }

    private static void OnTimeTextChanged(
        DependencyObject sender,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (sender is TimePickerField picker &&
            eventArgs.NewValue is string value)
        {
            picker.SynchronizeSelection(value);
        }
    }

    private void EntryButton_Checked(
        object sender,
        RoutedEventArgs eventArgs)
    {
        SynchronizeSelection(TimeText);
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () =>
            {
                BringSelectedItemsIntoView();
                HourList.Focus();
            });
    }

    private void TimePart_SelectionChanged(
        object sender,
        SelectionChangedEventArgs eventArgs)
    {
        if (_synchronizing ||
            HourList.SelectedItem is not int hour ||
            MinuteList.SelectedItem is not string minuteText ||
            PeriodList.SelectedItem is not string period ||
            !int.TryParse(
                minuteText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int minute))
        {
            return;
        }

        int hour24 = hour % 12;
        if (string.Equals(period, "PM", StringComparison.Ordinal))
        {
            hour24 += 12;
        }

        string display = new TimeOnly(hour24, minute).ToString(
            "h:mm tt",
            CultureInfo.CurrentCulture);
        ChosenTimeText.Text = display;
        SetCurrentValue(TimeTextProperty, display);
    }

    private void DoneButton_Click(
        object sender,
        RoutedEventArgs eventArgs)
    {
        EntryButton.IsChecked = false;
        EntryButton.Focus();
    }

    private void PickerPopup_Closed(
        object sender,
        EventArgs eventArgs)
    {
        EntryButton.IsChecked = false;
    }

    private void Root_PreviewKeyDown(
        object sender,
        KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Escape || !PickerPopup.IsOpen)
        {
            return;
        }

        EntryButton.IsChecked = false;
        EntryButton.Focus();
        eventArgs.Handled = true;
    }

    private void SynchronizeSelection(string? value)
    {
        if (!TryParseTime(value, out TimeOnly time))
        {
            ChosenTimeText.Text = value ?? string.Empty;
            return;
        }

        _synchronizing = true;
        try
        {
            int hour = time.Hour % 12;
            HourList.SelectedItem = hour == 0 ? 12 : hour;
            MinuteList.SelectedItem = time.Minute.ToString(
                "00",
                CultureInfo.InvariantCulture);
            PeriodList.SelectedItem = time.Hour < 12 ? "AM" : "PM";
            ChosenTimeText.Text = time.ToString(
                "h:mm tt",
                CultureInfo.CurrentCulture);
        }
        finally
        {
            _synchronizing = false;
        }
    }

    private static bool TryParseTime(
        string? value,
        out TimeOnly time)
    {
        return TimeOnly.TryParse(
                   value,
                   CultureInfo.CurrentCulture,
                   DateTimeStyles.AllowWhiteSpaces,
                   out time) ||
               TimeOnly.TryParse(
                   value,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AllowWhiteSpaces,
                   out time);
    }

    private void BringSelectedItemsIntoView()
    {
        HourList.ScrollIntoView(HourList.SelectedItem);
        MinuteList.ScrollIntoView(MinuteList.SelectedItem);
        PeriodList.ScrollIntoView(PeriodList.SelectedItem);
    }
}
