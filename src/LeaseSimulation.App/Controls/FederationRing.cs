using System;
using System.Collections.Generic;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using LeaseSimulation.Core;
using LeaseSimulation.Core.Models;

namespace LeaseSimulation.App.Controls;

public sealed class FederationRing : Panel
{
    private const double MinimumZoom = 0.5;
    private const double MaximumZoom = 8;

    public static readonly StyledProperty<IReadOnlyList<NodeSnapshot>?> NodesProperty =
        AvaloniaProperty.Register<FederationRing, IReadOnlyList<NodeSnapshot>?>(nameof(Nodes));

    public static readonly StyledProperty<IReadOnlyList<LeaseSnapshot>?> LeasesProperty =
        AvaloniaProperty.Register<FederationRing, IReadOnlyList<LeaseSnapshot>?>(nameof(Leases));

    public static readonly StyledProperty<ICommand?> ToggleNodeCommandProperty =
        AvaloniaProperty.Register<FederationRing, ICommand?>(nameof(ToggleNodeCommand));

    public static readonly StyledProperty<RingTopologySnapshot?> TopologyProperty =
        AvaloniaProperty.Register<FederationRing, RingTopologySnapshot?>(nameof(Topology));

    private static readonly IBrush RunningBrush = new SolidColorBrush(Color.Parse("#087E68"));
    private static readonly IBrush SuspectBrush = new SolidColorBrush(Color.Parse("#D79A20"));
    private static readonly IBrush DownBrush = new SolidColorBrush(Color.Parse("#D94B45"));
    private static readonly IBrush TextBrush = new SolidColorBrush(Colors.White);
    private readonly Cursor panCursor = new(StandardCursorType.Hand);
    private readonly Cursor panningCursor = new(StandardCursorType.SizeAll);
    private RingBackdrop? ringBackdrop;
    private LeaseBackdrop? leaseBackdrop;
    private double zoom = 1;
    private Vector panOffset;
    private Point lastPanPoint;
    private bool isPanning;
    private bool updateQueued;

    public FederationRing()
    {
        Background = Brushes.Transparent;
        ClipToBounds = true;
        Cursor = panCursor;
    }

    public IReadOnlyList<NodeSnapshot>? Nodes
    {
        get => GetValue(NodesProperty);
        set => SetValue(NodesProperty, value);
    }

    public IReadOnlyList<LeaseSnapshot>? Leases
    {
        get => GetValue(LeasesProperty);
        set => SetValue(LeasesProperty, value);
    }

    public ICommand? ToggleNodeCommand
    {
        get => GetValue(ToggleNodeCommandProperty);
        set => SetValue(ToggleNodeCommandProperty, value);
    }

    public RingTopologySnapshot? Topology
    {
        get => GetValue(TopologyProperty);
        set => SetValue(TopologyProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == NodesProperty ||
            change.Property == LeasesProperty ||
            change.Property == ToggleNodeCommandProperty ||
            change.Property == TopologyProperty)
        {
            QueueUpdateChildren();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
        {
            child.Measure(new Size(58, 58));
        }

        return new Size(
            double.IsInfinity(availableSize.Width) ? 500 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 500 : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count <= 2)
        {
            return finalSize;
        }

        Children[0].Arrange(new Rect(finalSize));
        Children[1].Arrange(new Rect(finalSize));
        panOffset = ClampPanOffset(panOffset, finalSize);
        UpdateBackdropViewport();
        var center = new Point(finalSize.Width / 2 + panOffset.X, finalSize.Height / 2 + panOffset.Y);
        var radius = Math.Max(0, Math.Min(finalSize.Width, finalSize.Height) / 2 - 46) * zoom;
        var nodeCount = Children.Count - 2;
        for (var index = 0; index < nodeCount; index++)
        {
            var point = Position(index, nodeCount, center, radius);
            Children[index + 2].Arrange(new Rect(point.X - 27, point.Y - 27, 54, 54));
        }

        return finalSize;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var previousZoom = zoom;
        zoom = Math.Clamp(zoom * Math.Pow(1.15, e.Delta.Y), MinimumZoom, MaximumZoom);
        if (Math.Abs(zoom - previousZoom) < double.Epsilon)
        {
            return;
        }

        var pointer = e.GetPosition(this);
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var pointerFromCenter = pointer - center;
        panOffset = pointerFromCenter - (pointerFromCenter - panOffset) * (zoom / previousZoom);
        panOffset = ClampPanOffset(panOffset, Bounds.Size);
        RefreshViewport();
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.Source != this || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        isPanning = true;
        lastPanPoint = e.GetPosition(this);
        Cursor = panningCursor;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!isPanning)
        {
            return;
        }

        var currentPoint = e.GetPosition(this);
        panOffset += currentPoint - lastPanPoint;
        panOffset = ClampPanOffset(panOffset, Bounds.Size);
        lastPanPoint = currentPoint;
        RefreshViewport();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!isPanning)
        {
            return;
        }

        isPanning = false;
        Cursor = panCursor;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        isPanning = false;
        Cursor = panCursor;
    }

    private void UpdateChildren()
    {
        var nodes = Nodes ?? [];
        if (ringBackdrop is null || leaseBackdrop is null || Children.Count != nodes.Count + 2)
        {
            RebuildChildren(nodes);
            return;
        }

        ringBackdrop.Nodes = nodes;
        ringBackdrop.Edges = Topology?.Edges ?? [];
        ringBackdrop.InvalidateVisual();
        leaseBackdrop.Nodes = nodes;
        leaseBackdrop.Leases = Leases ?? [];
        leaseBackdrop.InvalidateVisual();

        for (var index = 0; index < nodes.Count; index++)
        {
            UpdateNodeButton((Button)Children[index + 2], nodes[index]);
        }
    }

    private void RebuildChildren(IReadOnlyList<NodeSnapshot> nodes)
    {
        Children.Clear();
        ringBackdrop = new RingBackdrop
        {
            Nodes = nodes,
            Edges = Topology?.Edges ?? [],
            ViewScale = zoom,
            PanOffset = panOffset,
            IsHitTestVisible = false,
        };
        leaseBackdrop = new LeaseBackdrop
        {
            Nodes = nodes,
            Leases = Leases ?? [],
            ViewScale = zoom,
            PanOffset = panOffset,
            IsHitTestVisible = false,
        };
        Children.Add(ringBackdrop);
        Children.Add(leaseBackdrop);
        foreach (var node in nodes)
        {
            var button = new Button
            {
                Foreground = TextBrush,
                FontSize = 9,
                FontWeight = FontWeight.SemiBold,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
                CornerRadius = new CornerRadius(27),
                Padding = new Thickness(2),
            };
            UpdateNodeButton(button, node);
            Children.Add(button);
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    private void UpdateNodeButton(Button button, NodeSnapshot node)
    {
        var state = !node.IsRunning
            ? "CRASHED"
            : node.DownLeases > 0
                ? "DEGRADED"
                : node.SuspectLeases > 0
                    ? "SUSPECT"
                    : "HEALTHY";
        button.Content = $"{node.Id:00}\n{state}";
        button.Command = ToggleNodeCommand;
        button.CommandParameter = node.Id;
        button.Background = !node.IsRunning || node.DownLeases > 0
            ? DownBrush
            : node.SuspectLeases > 0
                ? SuspectBrush
                : RunningBrush;
        ToolTip.SetTip(button, node.IsRunning ? $"Crash node {node.Id:00}" : $"Recover node {node.Id:00}");
    }

    private void QueueUpdateChildren()
    {
        if (updateQueued)
        {
            return;
        }

        updateQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            updateQueued = false;
            UpdateChildren();
        }, DispatcherPriority.Render);
    }

    private void RefreshViewport()
    {
        UpdateBackdropViewport();
        InvalidateArrange();
    }

    private void UpdateBackdropViewport()
    {
        if (ringBackdrop is not null)
        {
            ringBackdrop.ViewScale = zoom;
            ringBackdrop.PanOffset = panOffset;
            ringBackdrop.InvalidateVisual();
        }

        if (leaseBackdrop is not null)
        {
            leaseBackdrop.ViewScale = zoom;
            leaseBackdrop.PanOffset = panOffset;
            leaseBackdrop.InvalidateVisual();
        }
    }

    private Vector ClampPanOffset(Vector offset, Size viewport)
    {
        var scaledRadius = Math.Max(0, Math.Min(viewport.Width, viewport.Height) / 2 - 46) * zoom;
        var contentRadius = scaledRadius + 27;
        var maximumX = Math.Max(0, contentRadius - viewport.Width / 2);
        var maximumY = Math.Max(0, contentRadius - viewport.Height / 2);
        return new Vector(
            Math.Clamp(offset.X, -maximumX, maximumX),
            Math.Clamp(offset.Y, -maximumY, maximumY));
    }

    private static Point Position(int index, int count, Point center, double radius)
    {
        var angle = -Math.PI / 2 + index * Math.PI * 2 / count;
        return new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
    }
}