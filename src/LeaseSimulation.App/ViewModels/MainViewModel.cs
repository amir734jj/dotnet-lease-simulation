using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Threading;
using LeaseSimulation.Core;
using LeaseSimulation.Core.Interfaces;
using LeaseSimulation.Core.Models;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;

namespace LeaseSimulation.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial int NodeCount { get; set; } = 12;

    [ObservableProperty]
    public partial int NeighborhoodSize { get; set; } = 4;

    [ObservableProperty]
    public partial double LeaseSeconds { get; set; } = 30;

    [ObservableProperty]
    public partial int RenewRatio { get; set; } = 6;

    [ObservableProperty]
    public partial int RetryCount { get; set; } = 3;

    [ObservableProperty]
    public partial bool ArbitrationEnabled { get; set; } = true;

    [ObservableProperty]
    public partial double ArbitrationSeconds { get; set; } = 30;

    [ObservableProperty]
    public partial int FirstPartitionSize { get; set; } = 6;

    [ObservableProperty]
    public partial double Speed { get; set; } = 10;

    [ObservableProperty]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial string ElapsedText { get; set; } = "00:00.0";

    [ObservableProperty]
    public partial string RunningText { get; set; } = "12 / 12";

    [ObservableProperty]
    public partial string SuspectedText { get; set; } = "0";

    [ObservableProperty]
    public partial string ConfirmedText { get; set; } = "0";

    [ObservableProperty]
    public partial string AverageDetectionText { get; set; } = "--";

    [ObservableProperty]
    public partial IReadOnlyList<NodeSnapshot> Nodes { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<LeaseSnapshot> Leases { get; set; } = [];

    [ObservableProperty]
    public partial RingTopologySnapshot Topology { get; set; } = new(0, [], []);

    [ObservableProperty]
    public partial string TopologyText { get; set; } = "RING v1";

    [ObservableProperty]
    public partial string RingPathText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string EventLogText { get; set; } = string.Empty;

    private readonly DispatcherTimer timer;
    private ILeaseClusterSimulator simulator = null!;
    private int displayedEventCount;

    public MainViewModel()
    {
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Reset();
    }

    public ObservableCollection<EventRow> EventRows { get; } = [];

    public string RunButtonText => IsRunning ? "Pause" : "Run";
    public string SpeedText => $"{Speed:0}x";
    public double RenewStartsAt => LeaseSeconds / RenewRatio;
    public int PartitionMaximum => Math.Max(1, NodeCount - 1);
    public string PartitionButtonText => simulator?.IsNetworkPartitioned == true ? "Heal partition" : "Start partition";

    [RelayCommand]
    private void ToggleRun()
    {
        IsRunning = !IsRunning;
        OnPropertyChanged(nameof(RunButtonText));
    }

    [RelayCommand]
    private void Step()
    {
        simulator.AdvanceBy(TimeSpan.FromSeconds(1));
        Refresh();
    }

    [RelayCommand]
    private void Reset()
    {
        IsRunning = false;
        NeighborhoodSize = Math.Min(NeighborhoodSize, NodeCount - 1);
        FirstPartitionSize = Math.Clamp(FirstPartitionSize, 1, PartitionMaximum);
        simulator = new LeaseClusterSimulator(new LeaseSimulationOptions
        {
            NodeCount = NodeCount,
            NeighborhoodSize = NeighborhoodSize,
            LeaseDuration = TimeSpan.FromSeconds(LeaseSeconds),
            LeaseRenewBeginRatio = RenewRatio,
            LeaseRetryCount = RetryCount,
            ArbitrationEnabled = ArbitrationEnabled,
            ArbitrationDuration = TimeSpan.FromSeconds(ArbitrationSeconds),
        });
        displayedEventCount = 0;
        EventRows.Clear();
        OnPropertyChanged(nameof(RunButtonText));
        OnPropertyChanged(nameof(PartitionButtonText));
        Refresh();
    }

    [RelayCommand]
    private void TogglePartition()
    {
        if (simulator.IsNetworkPartitioned)
        {
            simulator.HealNetworkPartition();
        }
        else
        {
            simulator.StartNetworkPartition(FirstPartitionSize - 1);
        }

        OnPropertyChanged(nameof(PartitionButtonText));
        Refresh();
    }

    [RelayCommand]
    private void ToggleNode(int nodeId)
    {
        var node = simulator.Nodes[nodeId];
        if (node.IsRunning)
        {
            simulator.CrashNode(nodeId);
        }
        else
        {
            simulator.RecoverNode(nodeId);
        }

        Refresh();
    }

    partial void OnSpeedChanged(double value)
    {
        OnPropertyChanged(nameof(SpeedText));
    }

    partial void OnLeaseSecondsChanged(double value)
    {
        OnPropertyChanged(nameof(RenewStartsAt));
    }

    partial void OnRenewRatioChanged(int value)
    {
        OnPropertyChanged(nameof(RenewStartsAt));
    }

    partial void OnNodeCountChanged(int value)
    {
        OnPropertyChanged(nameof(PartitionMaximum));
        FirstPartitionSize = Math.Clamp(FirstPartitionSize, 1, PartitionMaximum);
    }

    private void Tick()
    {
        if (!IsRunning)
        {
            return;
        }

        simulator.AdvanceBy(TimeSpan.FromMilliseconds(timer.Interval.TotalMilliseconds * Speed));
        Refresh();
    }

    private void Refresh()
    {
        var nodes = simulator.Nodes;
        Nodes = nodes;
        Leases = simulator.Leases;
        Topology = simulator.Topology;
        TopologyText = $"RING v{Topology.Version}  |  {Topology.MemberIds.Count} members";
        RingPathText = string.Join("  >  ", Topology.MemberIds.Select(nodeId => nodeId.ToString("00")));

        var newEvents = simulator.Events.Skip(displayedEventCount).ToArray();
        displayedEventCount = simulator.Events.Count;
        foreach (var item in newEvents)
        {
            EventRows.Insert(0, new EventRow(FormatTime(item.Time), item.Message));
        }

        while (EventRows.Count > 100)
        {
            EventRows.RemoveAt(EventRows.Count - 1);
        }

        EventLogText = string.Join(
            Environment.NewLine,
            EventRows.Select(row => $"{row.Time}  {row.Message}"));

        var detections = simulator.Detections;
        ElapsedText = FormatTime(simulator.Elapsed);
        RunningText = $"{nodes.Count(node => node.IsRunning)} / {nodes.Count}";
        SuspectedText = detections.Count.ToString();
        ConfirmedText = detections.Count(item => item.ConfirmedTime is not null).ToString();
        var confirmed = detections.Where(item => item.ConfirmationLatency is not null).ToArray();
        AverageDetectionText = confirmed.Length == 0
            ? "--"
            : $"{confirmed.Average(item => item.ConfirmationLatency!.Value.TotalSeconds):0.0}s";
    }

    private static string FormatTime(TimeSpan value) => $"{(int)value.TotalMinutes:00}:{value.Seconds:00}.{value.Milliseconds / 100}";
}
