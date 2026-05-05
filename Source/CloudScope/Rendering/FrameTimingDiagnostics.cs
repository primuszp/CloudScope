using System;
using System.Diagnostics;

namespace CloudScope.Rendering
{
    public sealed class FrameTimingDiagnostics
    {
        private readonly Stopwatch _stopwatch = new();
        private double _accumulatedSeconds;
        private int _frameCount;
        private int _drawCount;
        private double _mainDrawMs, _highlightMs, _previewMs, _gizmoMs, _swapMs;

        public bool Enabled { get; } =
            Environment.GetEnvironmentVariable("CLOUDSCOPE_FRAME_LOG") == "1";

        public void BeginFrame()
        {
            if (Enabled)
                _stopwatch.Restart();
        }

        public void MarkMainDraw(int drawCount)
        {
            _drawCount = drawCount;
            Mark(ref _mainDrawMs);
        }

        public void MarkHighlight() => Mark(ref _highlightMs);
        public void MarkPreview() => Mark(ref _previewMs);
        public void MarkGizmo() => Mark(ref _gizmoMs);
        public void MarkSwapAndLog(double frameSeconds)
        {
            Mark(ref _swapMs);
            Log(frameSeconds);
        }

        private void Mark(ref double stageMs)
        {
            if (!Enabled) return;

            stageMs += _stopwatch.Elapsed.TotalMilliseconds;
            _stopwatch.Restart();
        }

        private void Log(double frameSeconds)
        {
            if (!Enabled) return;

            _accumulatedSeconds += frameSeconds;
            _frameCount++;
            if (_accumulatedSeconds < 1.0)
                return;

            double inv = 1.0 / Math.Max(_frameCount, 1);
            Console.WriteLine(
                $"Frame avg {(_accumulatedSeconds * 1000.0 * inv):F2} ms | " +
                $"draw {_drawCount:N0} | main {_mainDrawMs * inv:F2} | " +
                $"hl {_highlightMs * inv:F2} | prev {_previewMs * inv:F2} | " +
                $"gizmo {_gizmoMs * inv:F2} | swap {_swapMs * inv:F2}");

            _accumulatedSeconds = 0;
            _frameCount = 0;
            _mainDrawMs = _highlightMs = _previewMs = _gizmoMs = _swapMs = 0;
        }
    }
}
