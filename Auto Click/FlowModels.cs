using System;
using System.Collections.Generic;
using System.Drawing;

namespace FlowRunner
{
    public enum FlowStepKind
    {
        Move,
        ClickLeft,
        ClickRight,
        Wheel,

        // Full-screen (virtual screen) checkpoint diff
        Checkpoint,

        // Verify a specific UI element exists (template present), otherwise show message / stop
        ExpectTemplate,
        TypeFlowNameAndEnter,
    }

    public sealed class FlowStep
    {
        public CheckpointRoiData? CheckpointRoi { get; set; }
        // ROI on VirtualScreen (absolute screen coords)
        public int RoiX { get; set; }
        public int RoiY { get; set; }
        public int RoiW { get; set; }
        public int RoiH { get; set; }
        public FlowStepKind Kind { get; set; }

        // Mouse coords
        public int X { get; set; }
        public int Y { get; set; }

        // Wheel only
        public int WheelDelta { get; set; }

        // Delay before executing this step (ms)
        public int DelayMs { get; set; }

        // Checkpoint only
        public string? Name { get; set; }
        public string? ImagePath { get; set; }

        // ===== ExpectTemplate only =====
        // relative to flow folder (e.g. "templates\\ok.png")
        public string? TemplatePath { get; set; }

        // Wait up to this time for the template to appear
        public int TimeoutMs { get; set; } = 5000;

        // Poll interval while waiting
        public int PollMs { get; set; } = 200;

        // 0..1 score threshold (higher = stricter)
        public double MatchThreshold { get; set; } = 0.90;

        // If true, stop the run when not found
        public bool StopOnFail { get; set; } = true;

        // Message to show when not found
        public string? FailMessage { get; set; }
    }

    public sealed class FlowDefinition
    {
        public string Category { get; set; } = "General";
        public string Name { get; set; } = "MyFlow";
        public int Loops { get; set; } = 1;

        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

        public List<FlowStep> Steps { get; set; } = new();
    }
    public struct RectDto
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public Rectangle ToRectangle() => new Rectangle(X, Y, Width, Height);
        public static RectDto FromRectangle(Rectangle r) => new RectDto
        {
            X = r.X,
            Y = r.Y,
            Width = r.Width,
            Height = r.Height
        };
    }

    public sealed class CheckpointRoiData
    {
        public RectDto Roi { get; set; }
        public string ExpectedPngBase64 { get; set; } = "";
        public double SimilarityThreshold { get; set; } = 0.995; // پیش‌فرض کم‌ریسک
    }
}