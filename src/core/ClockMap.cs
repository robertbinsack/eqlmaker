// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Time matching for an imported video (docs/time-matching.md):
//   video time  ->  wall clock of the recording machine (from the time stamps read in the frames)
//   wall clock of the recording machine  ->  clock of the database server (offset: measured, given,
//   or estimated from the data)
// The first mapping is piecewise linear: one straight line per uncut stretch of the video. Its slope
// absorbs drift between the recorder's frame clock and the wall clock; a jump between two
// neighbouring samples that the frame distance cannot explain is a cut.
using System;
using System.Collections.Generic;

namespace EqlMaker {

public class ClockSample { public double Video; public long WallUs; public bool FromDigits; }

public class ClockSegment {
    public double VideoFrom, VideoTo;   // samples of this stretch
    public double A;                    // wall seconds (relative to Base) at video time 0
    public double B = 1;                // wall seconds per video second
    public int Samples; public double ResidualMs;   // median absolute residual
    public long WallFromUs, WallToUs;
}

public class ClockMap {
    public List<ClockSegment> Segments = new List<ClockSegment>();
    /// <summary>Video times where the recording was cut (best estimate: between the two neighbouring samples).</summary>
    public List<double> Cuts = new List<double>();
    public long BaseUs;
    public int Rejected;

    /// <summary>Builds the mapping. cutThreshold: seconds by which the wall clock may deviate from the
    /// video clock between two neighbouring samples before a cut is assumed.</summary>
    public static ClockMap Fit(List<ClockSample> samples, double cutThreshold) {
        var map = new ClockMap();
        if (samples.Count == 0) return map;
        var s = new List<ClockSample>(samples);
        s.Sort(delegate (ClockSample a, ClockSample b) { return a.Video.CompareTo(b.Video); });
        map.BaseUs = s[0].WallUs;
        // single wrong readings (possible only through the digit fallback) are dropped: a sample that
        // disagrees with both neighbours while the neighbours agree with each other
        for (int i = 1; i + 1 < s.Count; i++) {
            double dPrev = (s[i].WallUs - s[i - 1].WallUs) / 1e6 - (s[i].Video - s[i - 1].Video);
            double dNext = (s[i + 1].WallUs - s[i].WallUs) / 1e6 - (s[i + 1].Video - s[i].Video);
            double dSkip = (s[i + 1].WallUs - s[i - 1].WallUs) / 1e6 - (s[i + 1].Video - s[i - 1].Video);
            if (Math.Abs(dPrev) > cutThreshold && Math.Abs(dNext) > cutThreshold && Math.Abs(dSkip) <= cutThreshold) { s.RemoveAt(i); map.Rejected++; i--; }
        }
        int start = 0;
        for (int i = 1; i <= s.Count; i++) {
            bool cut = i == s.Count;
            if (!cut) {
                double d = (s[i].WallUs - s[i - 1].WallUs) / 1e6 - (s[i].Video - s[i - 1].Video);
                cut = Math.Abs(d) > cutThreshold;
                if (cut) map.Cuts.Add((s[i - 1].Video + s[i].Video) / 2);
            }
            if (!cut) continue;
            map.Segments.Add(FitSegment(s, start, i - 1, map.BaseUs));
            start = i;
        }
        return map;
    }

    static ClockSegment FitSegment(List<ClockSample> s, int from, int to, long baseUs) {
        var seg = new ClockSegment { VideoFrom = s[from].Video, VideoTo = s[to].Video, Samples = to - from + 1, WallFromUs = s[from].WallUs, WallToUs = s[to].WallUs };
        int n = seg.Samples;
        if (n < 2 || seg.VideoTo - seg.VideoFrom < 2.0) {
            // too short for a slope: wall clock and video clock run alike here
            var offs = new List<double>(); for (int i = from; i <= to; i++) offs.Add((s[i].WallUs - baseUs) / 1e6 - s[i].Video);
            offs.Sort(); seg.A = offs[offs.Count / 2]; seg.B = 1;
        } else {
            // least squares, then once more without the samples that lie far off
            double a = 0, b = 1;
            var use = new List<int>(); for (int i = from; i <= to; i++) use.Add(i);
            for (int pass = 0; pass < 2; pass++) {
                double sx = 0, sy = 0, sxx = 0, sxy = 0; int m = use.Count;
                foreach (int i in use) { double x = s[i].Video - seg.VideoFrom, y = (s[i].WallUs - baseUs) / 1e6; sx += x; sy += y; sxx += x * x; sxy += x * y; }
                double den = m * sxx - sx * sx;
                b = den == 0 ? 1 : (m * sxy - sx * sy) / den;
                double a0 = (sy - b * sx) / m;   // at VideoFrom
                a = a0 - b * seg.VideoFrom;
                if (pass == 0) {
                    var res = new List<double>(); foreach (int i in use) res.Add(Math.Abs((s[i].WallUs - baseUs) / 1e6 - (a + b * s[i].Video)));
                    var sorted = new List<double>(res); sorted.Sort(); double med = sorted[sorted.Count / 2], lim = Math.Max(0.02, med * 4);
                    var keep = new List<int>(); for (int k = 0; k < use.Count; k++) if (res[k] <= lim) keep.Add(use[k]);
                    if (keep.Count >= 2 && keep.Count < use.Count) use = keep; else break;
                }
            }
            seg.A = a; seg.B = b;
        }
        var r = new List<double>(); for (int i = from; i <= to; i++) r.Add(Math.Abs((s[i].WallUs - baseUs) / 1e6 - (seg.A + seg.B * s[i].Video)));
        r.Sort(); seg.ResidualMs = r[r.Count / 2] * 1000;
        return seg;
    }

    /// <summary>Sets the exact position of a cut (found by looking at every frame around it).</summary>
    public void RefineCut(int index, double videoTime) { if (index >= 0 && index < Cuts.Count) Cuts[index] = videoTime; }

    int SegmentAt(double video) {
        if (Segments.Count == 0) return -1;
        for (int i = 0; i < Cuts.Count; i++) if (video < Cuts[i]) return i;
        return Segments.Count - 1;
    }

    /// <summary>Wall clock (microseconds since 1970 UTC) of a video time.</summary>
    public long ToWallUs(double video) {
        int i = SegmentAt(video);
        if (i < 0) throw new Exception("no time stamps were read from the video");
        var g = Segments[i];
        return BaseUs + (long)Math.Round((g.A + g.B * video) * 1e6);
    }

    /// <summary>Video time of a wall clock time; NaN when that moment is not in the video (before, after, or cut out).</summary>
    public double ToVideo(long wallUs, double tolerance) {
        for (int i = 0; i < Segments.Count; i++) {
            var g = Segments[i];
            double v = ((wallUs - BaseUs) / 1e6 - g.A) / g.B;
            double lo = i == 0 ? g.VideoFrom - tolerance : Cuts[i - 1], hi = i == Segments.Count - 1 ? g.VideoTo + tolerance : Cuts[i];
            if (v >= lo && v <= hi) return v;
        }
        return double.NaN;
    }

    public JO ToJson() {
        var segs = new List<object>();
        foreach (var g in Segments) segs.Add(new JO("video_from", Math.Round(g.VideoFrom, 3), "video_to", Math.Round(g.VideoTo, 3), "wall_from", Clock.Iso(g.WallFromUs), "wall_to", Clock.Iso(g.WallToUs),
            "wall_at_video_0_us", BaseUs + (long)Math.Round(g.A * 1e6), "rate", g.B, "drift_ppm", Math.Round((g.B - 1) * 1e6, 1), "samples", g.Samples, "median_residual_ms", Math.Round(g.ResidualMs, 2)));
        var cuts = new List<object>(); foreach (var c in Cuts) cuts.Add(Math.Round(c, 3));
        return new JO("segments", segs, "cuts", cuts, "rejected_samples", Rejected);
    }

    // ------------------------------------------------------------ offset between two clocks
    /// <summary>Estimates the constant offset between two clocks from two lists of moments that belong
    /// together loosely (e.g. moments at which the screen changed, and moments at which the database was
    /// written): the shift s for which most pairs a + s = b agree. Times in seconds.
    /// Returns the shift to ADD to a; confidence = share of the a-moments that find a partner at that shift
    /// relative to the next best shift (1 = no better than chance).</summary>
    public static double EstimateOffset(List<double> a, List<double> b, double maxShift, double tolerance, out double confidence, out int matched) {
        confidence = 0; matched = 0;
        if (a.Count == 0 || b.Count == 0) return double.NaN;
        var sb = new List<double>(b); sb.Sort();
        // histogram of all differences b - a within +/- maxShift
        double bin = tolerance; int bins = (int)Math.Ceiling(2 * maxShift / bin) + 1;
        var hist = new int[bins];
        foreach (double x in a) {
            int lo = LowerBound(sb, x - maxShift);
            for (int j = lo; j < sb.Count && sb[j] <= x + maxShift; j++) hist[(int)((sb[j] - x + maxShift) / bin)]++;
        }
        // best window of two neighbouring bins
        int best = 0, bestVal = -1;
        for (int i = 0; i + 1 < bins; i++) { int v = hist[i] + hist[i + 1]; if (v > bestVal) { bestVal = v; best = i; } }
        int second = 0;
        for (int i = 0; i + 1 < bins; i++) { if (Math.Abs(i - best) <= 2) continue; int v = hist[i] + hist[i + 1]; if (v > second) second = v; }
        // refine: median of the differences inside the best window
        double wLo = best * bin - maxShift, wHi = wLo + 2 * bin; var diffs = new List<double>();
        foreach (double x in a) {
            int lo = LowerBound(sb, x + wLo); double bestD = double.NaN;
            for (int j = lo; j < sb.Count && sb[j] <= x + wHi; j++) { double d = sb[j] - x; if (double.IsNaN(bestD) || Math.Abs(d - (wLo + bin)) < Math.Abs(bestD - (wLo + bin))) bestD = d; }
            if (!double.IsNaN(bestD)) diffs.Add(bestD);
        }
        if (diffs.Count == 0) return double.NaN;
        diffs.Sort(); matched = diffs.Count;
        confidence = second == 0 ? bestVal : (double)bestVal / second;
        return diffs[diffs.Count / 2];
    }
    static int LowerBound(List<double> l, double v) { int lo = 0, hi = l.Count; while (lo < hi) { int mid = (lo + hi) / 2; if (l[mid] < v) lo = mid + 1; else hi = mid; } return lo; }
}
}
