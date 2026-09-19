using System.Media;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using NevaVision.Core;

namespace NevaVision;

public partial class MainWindow : Window
{
    private readonly Rf4ObservationProvider _provider = new();
    private readonly OverlayWindow _overlay = new();
    private readonly OverlayOptions _options = new();
    private readonly DispatcherTimer _timer;
    private readonly bool[] _previousHooked = new bool[3];
    private ObservationSnapshot _lastSnapshot = new(
        DateTimeOffset.Now,
        false,
        null,
        "Демо-режим",
        Array.Empty<FishObservation>());

    public MainWindow()
    {
        InitializeComponent();

        _overlay.Left = 28;
        _overlay.Top = 96;
        _overlay.Show();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += Timer_Tick;
        Loaded += (_, _) =>
        {
            SyncOptions();
            _timer.Start();
            Timer_Tick(this, EventArgs.Empty);
        };
        Closing += MainWindow_Closing;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        try
        {
            bool demo = DemoCheckBox.IsChecked == true;
            _lastSnapshot = demo ? CreateDemoSnapshot() : _provider.Capture();
            UpdateView(_lastSnapshot);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Ошибка чтения: {ex.Message}";
        }
    }

    private ObservationSnapshot CreateDemoSnapshot()
    {
        bool active = (DateTimeOffset.Now.ToUnixTimeSeconds() % 12) < 8;
        var observations = active
            ? new[]
            {
                new FishObservation(
                    RodIndex: 0,
                    IsActive: true,
                    IsHooked: true,
                    FishId: "e_chub",
                    FishName: "Голавль",
                    WeightKg: 0.27,
                    DistanceMeters: 6.33,
                    Slot: null,
                    IsRare: false)
            }
            : Array.Empty<FishObservation>();

        return new ObservationSnapshot(
            DateTimeOffset.Now,
            Attached: false,
            ProcessId: null,
            Status: active ? "Демо: рыба обнаружена" : "Демо: ожидание поклёвки",
            Observations: observations);
    }

    private void UpdateView(ObservationSnapshot snapshot)
    {
        SyncOptions();
        StatusText.Text = snapshot.Status;
        ProcessText.Text = snapshot.ProcessId is int pid ? $"PID: {pid}" : "PID: —";

        var activeNow = new bool[_previousHooked.Length];
        foreach (FishObservation observation in snapshot.Observations)
        {
            if (observation.RodIndex is >= 0 and < 3)
                activeNow[observation.RodIndex] = observation.IsHooked;

            if (_options.Sound && observation.IsHooked && !_previousHooked.ElementAtOrDefault(observation.RodIndex))
                SystemSounds.Asterisk.Play();
        }

        Array.Copy(activeNow, _previousHooked, activeNow.Length);

        string snapshotText = snapshot.Observations.Count == 0
            ? "Активных меток нет"
            : string.Join(Environment.NewLine, snapshot.Observations.Select(BuildLabel));
        SnapshotText.Text = snapshotText;

        if (!_options.ShowFish)
        {
            _overlay.UpdateContent("Отображение рыбы выключено", snapshot.Status);
            return;
        }

        if (snapshot.Observations.Count == 0)
        {
            _overlay.UpdateContent("Ожидание поклёвки…", snapshot.Status);
            return;
        }

        var labels = snapshot.Observations.Select(BuildLabel).ToArray();
        _overlay.UpdateContent(string.Join(Environment.NewLine, labels), snapshot.Status);
    }

    private string BuildLabel(FishObservation observation)
    {
        var builder = new StringBuilder();

        if (_options.ShowRodSlot && observation.Slot is int slot)
            builder.Append('[').Append(slot).Append("] ");

        if (_options.ShowName)
            builder.Append(observation.FishName);
        else if (_options.ShowFish)
            builder.Append("Рыба");

        if (_options.ShowRarity && observation.IsRare)
            builder.Append(" • редкая");

        if (_options.ShowWeight && observation.WeightKg is double weight)
            builder.Append(' ').Append(weight.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).Append(" kg");

        if (_options.ShowDistance)
        {
            string distance = observation.DistanceMeters is double value
                ? value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                : "—";
            builder.Append(' ').Append(distance).Append(" m");
        }

        if (_options.BiteAssistant && observation.IsHooked)
            builder.Append("  • поклёвка");

        return builder.ToString().Trim();
    }

    private void AttachButton_Click(object sender, RoutedEventArgs e)
    {
        if (_provider.TryAttach(out string status))
        {
            DemoCheckBox.IsChecked = false;
            StatusText.Text = status;
        }
        else
        {
            StatusText.Text = status;
        }
    }

    private void OptionChanged(object sender, RoutedEventArgs e)
    {
        SyncOptions();
        UpdateView(_lastSnapshot);
    }

    private void SyncOptions()
    {
        _options.ShowFish = ShowFishCheckBox.IsChecked == true;
        _options.ShowName = ShowNameCheckBox.IsChecked == true;
        _options.ShowWeight = ShowWeightCheckBox.IsChecked == true;
        _options.ShowDistance = ShowDistanceCheckBox.IsChecked == true;
        _options.ShowRarity = ShowRarityCheckBox.IsChecked == true;
        _options.Sound = SoundCheckBox.IsChecked == true;
        _options.ShowRodSlot = ShowSlotCheckBox.IsChecked == true;
        _options.BiteAssistant = BiteAssistantCheckBox.IsChecked == true;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _timer.Stop();
        _overlay.Close();
        _provider.Dispose();
    }
}
