using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LeaseSimulation.Core.Models;

namespace LeaseSimulation.App.Controls;

internal sealed class RingBackdrop : Control
{
    private static readonly IBrush ActiveEdgeBrush = new SolidColorBrush(Color.Parse("#69AFA0"));
    private static readonly IBrush PendingEdgeBrush = new SolidColorBrush(Color.Parse("#D79A20"));

    public IReadOnlyList<NodeSnapshot> Nodes { get; set; } = [];
    public IReadOnlyList<RingEdgeSnapshot> Edges { get; set; } = [];
    public double ViewScale { get; set; } = 1;
    public Vector PanOffset { get; set; }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var center = new Point(Bounds.Width / 2 + PanOffset.X, Bounds.Height / 2 + PanOffset.Y);
        var radius = Math.Max(0, Math.Min(Bounds.Width, Bounds.Height) / 2 - 46) * ViewScale;
        if (Nodes.Count <= 1)
        {
            return;
        }

        foreach (var edge in Edges)
        {
            var from = Position(edge.FromNodeId, Nodes.Count, center, radius);
            var to = Position(edge.ToNodeId, Nodes.Count, center, radius);
            var hasCrashedEndpoint = !Nodes[edge.FromNodeId].IsRunning || !Nodes[edge.ToNodeId].IsRunning;
            context.DrawLine(new Pen(hasCrashedEndpoint ? PendingEdgeBrush : ActiveEdgeBrush, 3), from, to);
        }
    }

    private static Point Position(int index, int count, Point center, double radius)
    {
        var angle = -Math.PI / 2 + index * Math.PI * 2 / count;
        return new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
    }
}