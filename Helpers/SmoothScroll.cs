using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace SoundCheck.Helpers;

/// <summary>
/// Smooth scrolling for any ListView / ScrollViewer.
///
/// Model: a "smoothed-target chase".  The wheel directly nudges a virtual
/// target offset; on every frame the actual ScrollViewer offset moves toward
/// that target with exponential decay (critical-damping-ish feel).
///
/// Why this beats a velocity/friction model:
///   • Frame-rate independent — exp(-dt*k) gives identical feel at 60, 120, 144Hz
///   • No "stop-and-go" between wheel notches — target moves once per notch,
///     actual offset smoothly chases it. Slow spins feel buttery, not jerky.
///   • Fast spins accumulate target ahead and the offset catches up with a
///     natural ease-out arc.
///
/// All tunables live near the top of the file.
/// </summary>
public static class SmoothScroll
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(SmoothScroll),
            new PropertyMetadata(false, OnEnabledChanged));
    public static void SetEnabled(DependencyObject d, bool v) => d.SetValue(EnabledProperty, v);
    public static bool GetEnabled(DependencyObject d) => (bool)d.GetValue(EnabledProperty);

    // ── Tunables ────────────────────────────────────────────────────────────
    // Pixels per wheel notch (Delta=120). Browsers default to ~100; 80 feels
    // slightly more refined and is closer to ~3 rows of our list.
    private const double StepPerNotch = 80.0;
    // How quickly the actual offset chases the target. Higher = snappier.
    //   8  = soft/wet (think Notion)
    //   12 = balanced macOS-ish
    //   18 = crisp & precise
    private const double Stiffness = 12.0;
    // Below this offset-delta we stop ticking and snap to the target. Should
    // be sub-pixel so the final settle is invisible.
    private const double SettleEpsilon = 0.3;
    // When successive notches arrive faster than this, treat as a "fast spin"
    // and amplify the per-notch step (linear ramp up to MaxBoost).
    private const double FastSpinWindowMs = 110;
    private const double MaxBoost = 1.9;

    private sealed class Chase
    {
        public ScrollViewer Sv = null!;
        public double Target;
        public double Current;
        public bool Ticking;
        public DateTime LastWheelTime = DateTime.MinValue;
        public int LastWheelSign;
        public DateTime LastFrameTime;
        public EventHandler? Tick;
    }

    private static readonly Dictionary<ScrollViewer, Chase> _states = new();

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el) return;
        if ((bool)e.NewValue) el.PreviewMouseWheel += OnPreviewMouseWheel;
        else el.PreviewMouseWheel -= OnPreviewMouseWheel;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var sv = FindDescendantScrollViewer(sender as DependencyObject);
        if (sv == null || sv.ScrollableHeight <= 0) return;

        if (!_states.TryGetValue(sv, out var s))
        {
            s = new Chase { Sv = sv, Target = sv.VerticalOffset, Current = sv.VerticalOffset };
            // Capture in delegate so we can detach the exact handler on Unloaded.
            s.Tick = (_, _) => OnTick(s);
            _states[sv] = s;
            sv.Unloaded += (_, _) =>
            {
                if (s.Ticking && s.Tick != null) CompositionTarget.Rendering -= s.Tick;
                _states.Remove(sv);
            };
        }

        // If the chase is idle, the ScrollViewer may have been moved externally
        // (e.g. ScrollIntoView when switching tracks). Re-anchor to the real offset
        // so the next wheel notch continues from where the list actually is —
        // otherwise the stale target snaps the list back to the top.
        if (!s.Ticking)
        {
            s.Current = sv.VerticalOffset;
            s.Target = sv.VerticalOffset;
        }

        int sign = e.Delta > 0 ? -1 : 1;
        double notches = Math.Abs(e.Delta) / 120.0;
        double step = StepPerNotch * notches;

        var now = DateTime.UtcNow;
        if (sign == s.LastWheelSign)
        {
            double gapMs = (now - s.LastWheelTime).TotalMilliseconds;
            if (gapMs < FastSpinWindowMs)
            {
                double t = 1.0 - (gapMs / FastSpinWindowMs);
                step *= 1.0 + (MaxBoost - 1.0) * t;
            }
        }
        else
        {
            // Reversed direction — re-anchor target to current offset so the
            // user gets immediate feedback instead of fighting old momentum.
            s.Target = s.Current;
        }

        s.Target = Math.Max(0, Math.Min(sv.ScrollableHeight, s.Target + sign * step));
        s.LastWheelTime = now;
        s.LastWheelSign = sign;

        if (!s.Ticking)
        {
            s.Ticking = true;
            s.LastFrameTime = now;
            CompositionTarget.Rendering += s.Tick!;
        }
        e.Handled = true;
    }

    private static void OnTick(Chase s)
    {
        var now = DateTime.UtcNow;
        double dt = (now - s.LastFrameTime).TotalSeconds;
        s.LastFrameTime = now;
        // Clamp dt — protects against huge delta after sleep/breakpoint resume.
        if (dt > 0.05) dt = 0.05;
        if (dt <= 0) return;

        // Re-sync current with what the ScrollViewer actually shows in case
        // something else moved it (e.g. ScrollIntoView from PlayTrack).
        // We only do this when the difference is large enough to matter — small
        // discrepancies come from sub-pixel rendering and shouldn't reset us.
        if (Math.Abs(s.Current - s.Sv.VerticalOffset) > 4)
            s.Current = s.Sv.VerticalOffset;

        // Clamp target again in case content was resized between frames.
        double max = s.Sv.ScrollableHeight;
        if (s.Target > max) s.Target = max;
        if (s.Target < 0) s.Target = 0;

        double diff = s.Target - s.Current;
        if (Math.Abs(diff) < SettleEpsilon)
        {
            s.Current = s.Target;
            s.Sv.ScrollToVerticalOffset(s.Current);
            s.Ticking = false;
            if (s.Tick != null) CompositionTarget.Rendering -= s.Tick;
            return;
        }

        // Exponential approach: current += diff * (1 - e^(-stiffness*dt))
        // Frame-rate-independent: same visual feel at 60 / 120 / 144Hz.
        double factor = 1.0 - Math.Exp(-Stiffness * dt);
        s.Current += diff * factor;
        s.Sv.ScrollToVerticalOffset(s.Current);
    }

    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject? root)
    {
        if (root == null) return null;
        if (root is ScrollViewer sv) return sv;
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var found = FindDescendantScrollViewer(child);
            if (found != null) return found;
        }
        return null;
    }
}
