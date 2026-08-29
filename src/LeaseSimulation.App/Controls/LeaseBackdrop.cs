using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LeaseSimulation.Core.Models;

namespace LeaseSimulation.App.Controls;

internal sealed class LeaseBackdrop : Control
{
    private static readonly IBrush ActiveBrush = new SolidColorBrush(Color.Parse("#702B78A0"));
    private static readonly IBrush RenewingBrush = new SolidColorBrush(Color.Parse("#D79A20"));
    private static readonly IBrush ArbitratingBrush = new SolidColorBrush(Color.Parse("#D94B45"));
    private static readonly IBrush DownBrush = new SolidColorBrush(Color.Parse("#5E6B67"));

    public IReadOnlyList<NodeSnapshot> Nodes { get; set; } = [];
    public IReadOnlyList<LeaseSnapshot> Leases { get; set; } = [];
    public double ViewScale { get; set; } = 1;
    public Vector PanOffset { get; set; }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Nodes.Count <= 1)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2 + PanOffset.X, Bounds.Height / 2 + PanOffset.Y);
        var radius = Math.Max(0, Math.Min(Bounds.Width, Bounds.Height) / 2 - 46) * ViewScale;
        foreach (var lease in Leases)
        {
            DrawLease(context, lease, center, radius);
        }
    }

    private void DrawLease(DrawingContext context, LeaseSnapshot lease, Point center, double radius)
    {
        var fromCenter = Position(lease.SourceId, Nodes.Count, center, radius);
        var toCenter = Position(lease.TargetId, Nodes.Count, center, radius);
        var delta = toCenter - fromCenter;
        var length = Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y);
        if (length <= 62)
        {
            return;
        }

        var direction = new Vector(delta.X / length, delta.Y / length);
        var normal = new Vector(-direction.Y, direction.X);
        var start = fromCenter + direction * 31 + normal * 4;
        var end = toCenter - direction * 31 + normal * 4;
        var brush = BrushFor(lease.Status);
        var thickness = lease.Status == LeaseStatus.Active ? 1 : 2;
        var pen = new Pen(brush, thickness);

        context.DrawLine(pen, start, end);

        const double arrowLength = 7;
        const double arrowWidth = 4;
        var arrowBase = end - direction * arrowLength;
        context.DrawLine(pen, end, arrowBase + normal * arrowWidth);
        context.DrawLine(pen, end, arrowBase - normal * arrowWidth);
    }

    private static IBrush BrushFor(LeaseStatus status) => status switch
    {
        LeaseStatus.Active => ActiveBrush,
        LeaseStatus.Renewing => RenewingBrush,
        LeaseStatus.Arbitrating => ArbitratingBrush,
        LeaseStatus.Down => DownBrush,
        _ => ActiveBrush,
    };

    private static Point Position(int index, int count, Point center, double radius)
    {
        var angle = -Math.PI / 2 + index * Math.PI * 2 / count;
        return new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
    }
}