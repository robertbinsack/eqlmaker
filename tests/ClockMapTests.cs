// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (c) 2026 Robert Binsack
//
// Time matching with numbers. The model of a screen recording used here:
//   - the recorder writes frames at a nominal rate; its clock may drift against the wall clock
//   - a frame shows the overlay as the display showed it at capture time: the overlay repaints every
//     2 ms, the display refreshes at 60 Hz, so the time in the picture is 0 to 18.7 ms old
//   - a cut removes a stretch of wall time; video time runs on without a gap
using System;
using System.Collections.Generic;
using System.Globalization;

namespace EqlMaker.Tests {

public static class ClockMapTests {
    const long W0 = 1795630000000000L;   // wall clock at video time 0

    class Sim {
        public double Fps, DriftPpm; public List<double[]> Cuts = new List<double[]>();   // wall seconds [from, to) removed
        public Random Rnd = new Random(11);
        /// <summary>True wall time (seconds after W0) of a video time.</summary>
        public double Truth(double video) {
            double wall = video * (1 + DriftPpm / 1e6);
            foreach (var c in Cuts) if (wall >= c[0]) wall += c[1] - c[0];
            return wall;
        }
        public double CutVideoTime(int i) {   // video time at which cut i happens
            double w = Cuts[i][0]; for (int k = 0; k < i; k++) w -= Cuts[k][1] - Cuts[k][0];
            return w / (1 + DriftPpm / 1e6);
        }
        /// <summary>What the frame at this video time shows, in whole milliseconds.</summary>
        public long Shown(double video) {
            double wallMs = Truth(video) * 1000;
            double painted = wallMs - Rnd.NextDouble() * 2;                // overlay repaint interval
            double refresh = Math.Floor(painted / (1000.0 / 60)) * (1000.0 / 60);   // display refresh before the capture
            double shown = Math.Floor(refresh - Rnd.NextDouble() * 2);
            return (long)shown;
        }
        public List<ClockSample> Samples(double duration, double sampleFps) {
            var l = new List<ClockSample>(); int step = Math.Max(1, (int)Math.Round(Fps / sampleFps));
            for (int k = 0; k / Fps < duration; k += step) { double v = k / Fps; l.Add(new ClockSample { Video = v, WallUs = W0 + Shown(v) * 1000 }); }
            return l;
        }
    }

    static void Errors(Sim sim, ClockMap map, double duration, double skipAroundCuts, out double mean, out double max) {
        double sum = 0; max = 0; int n = 0;
        for (double v = 0.05; v < duration; v += 0.0731) {
            bool near = false; for (int i = 0; i < sim.Cuts.Count; i++) if (Math.Abs(v - sim.CutVideoTime(i)) < skipAroundCuts) near = true;
            if (near) continue;
            double err = (map.ToWallUs(v) - W0) / 1000.0 - sim.Truth(v) * 1000;
            sum += err; n++; if (Math.Abs(err) > max) max = Math.Abs(err);
        }
        mean = sum / n;
    }
    static string F(double v) { return v.ToString("0.0", CultureInfo.InvariantCulture); }

    public static void UncutVideoAt15_30_60FramesPerSecond() {
        foreach (double fps in new[] { 15.0, 30.0, 60.0 }) {
            var sim = new Sim { Fps = fps, DriftPpm = 80 };
            var map = ClockMap.Fit(sim.Samples(300, 2), Math.Max(0.25, 3 / fps));
            T.Eq(1, map.Segments.Count, "one stretch at " + fps + " fps"); T.Eq(0, map.Cuts.Count, "no cut");
            double mean, max; Errors(sim, map, 300, 0, out mean, out max);
            T.Note("fps" + (int)fps + "_error_ms_mean_max", F(mean) + " / " + F(max));
            T.Note("fps" + (int)fps + "_drift_ppm_found", F((map.Segments[0].B - 1) * 1e6));
            T.True(max < 25, "error stays below 25 ms at " + fps + " fps: " + max);
            T.True(mean < 0 && mean > -20, "the time in the picture is a few milliseconds old (mean " + mean + ")");
            T.Near(80, (map.Segments[0].B - 1) * 1e6, 15, "drift found");
        }
    }

    public static void StrongDriftIsFollowed() {
        var sim = new Sim { Fps = 30, DriftPpm = 1500 };   // 0.15 %: 90 ms per minute
        var map = ClockMap.Fit(sim.Samples(600, 2), 0.25);
        double mean, max; Errors(sim, map, 600, 0, out mean, out max);
        T.Note("drift1500ppm_error_ms_mean_max", F(mean) + " / " + F(max));
        T.True(max < 25 && map.Segments.Count == 1, "no false cut and small error with strong drift: " + max);
        // without the slope the error would be the whole drift
        T.True(Math.Abs(600 * 1500 / 1e6 * 1000) > 800, "for comparison: ignoring drift would be wrong by 900 ms at the end");
    }

    public static void CutsAreFoundAndMappedOnBothSides() {
        foreach (double fps in new[] { 15.0, 30.0, 60.0 }) {
            var sim = new Sim { Fps = fps, DriftPpm = 40 };
            sim.Cuts.Add(new[] { 20.0, 35.5 });      // 15.5 s cut out
            sim.Cuts.Add(new[] { 80.0, 80.9 });      // a short cut of 0.9 s
            sim.Cuts.Add(new[] { 200.0, 4000.0 });   // a long break
            var map = ClockMap.Fit(sim.Samples(260, 2), Math.Max(0.25, 3 / fps));
            T.Eq(3, map.Cuts.Count, "cuts found at " + fps + " fps"); T.Eq(4, map.Segments.Count, "stretches");
            double worst = 0;
            for (int i = 0; i < 3; i++) worst = Math.Max(worst, Math.Abs(map.Cuts[i] - sim.CutVideoTime(i)));
            T.True(worst <= 0.26, "cut position known to half a sample interval before refinement: " + worst);
            double mean, max; Errors(sim, map, 260, 0.51, out mean, out max);
            T.Note("cuts_fps" + (int)fps + "_error_ms_mean_max_away_from_cuts", F(mean) + " / " + F(max));
            T.True(max < 25, "error away from the cuts: " + max);
            // with the exact cut positions (found by reading every frame around a cut) the whole video maps
            for (int i = 0; i < 3; i++) map.RefineCut(i, sim.CutVideoTime(i));
            Errors(sim, map, 260, 0, out mean, out max);
            T.Note("cuts_fps" + (int)fps + "_error_ms_mean_max_refined", F(mean) + " / " + F(max));
            T.True(max < 25, "error everywhere after refinement: " + max);
            // moments inside a cut are not in the video
            T.True(double.IsNaN(map.ToVideo(W0 + 30 * 1000000L, 0.5)), "a moment that was cut out has no video time");
            T.Near(sim.CutVideoTime(0) + 4.5 / (1 + 40e-6), map.ToVideo(W0 + 40 * 1000000L, 0.5), 0.03, "wall time after the cut maps back to the video");
        }
    }

    public static void AWrongDigitReadingIsDropped() {
        var sim = new Sim { Fps = 30, DriftPpm = 0 };
        var s = sim.Samples(60, 2);
        s[40].WallUs += 10 * 60 * 1000000L; s[40].FromDigits = true;   // one reading is ten minutes off
        var map = ClockMap.Fit(s, 0.25);
        T.Eq(1, map.Rejected, "rejected"); T.Eq(0, map.Cuts.Count, "not taken for two cuts");
        double mean, max; Errors(sim, map, 60, 0, out mean, out max);
        T.True(max < 25, "error after dropping: " + max);
    }

    /// <summary>The recording machine's clock is wrong by an unknown amount; the offset to the database
    /// server's clock is estimated from moments the screen changed and moments the database was used.</summary>
    public static void OffsetOfAWrongClockIsEstimated() {
        string numbers = "";
        foreach (double trueOffset in new[] { 37.4, -212.0, 0.35 }) {
            var rnd = new Random(5);
            var db = new List<double>(); var screen = new List<double>(); double t = 10;
            double latSum = 0; int n = 0;
            for (int i = 0; i < 45; i++) {
                t += 3 + rnd.NextDouble() * 25;                       // an operation every 3 to 28 s
                int stm = 1 + rnd.Next(4);
                for (int k = 0; k < stm; k++) db.Add(t + k * 0.02);   // a burst of statements
                double latency = 0.08 + rnd.NextDouble() * 0.25;      // the screen shows the result 80 to 330 ms later
                latSum += latency; n++;
                screen.Add(t + latency - trueOffset);                 // seen through the wrong clock
            }
            for (int i = 0; i < 30; i++) screen.Add(rnd.NextDouble() * t - trueOffset);   // screen changes without database access (typing, menus)
            for (int i = 0; i < 20; i++) db.Add(rnd.NextDouble() * t);                    // statements of other users
            double conf; int matched;
            double est = ClockMap.EstimateOffset(screen, db, 600, 0.25, out conf, out matched);
            double errMs = (est - trueOffset) * 1000, meanLat = latSum / n * 1000;
            numbers += " true " + trueOffset.ToString("0.00", CultureInfo.InvariantCulture) + " s: error " + F(errMs) + " ms (mean screen latency " + F(meanLat) + " ms), confidence " + conf.ToString("0.0", CultureInfo.InvariantCulture) + ";";
            T.True(Math.Abs(errMs) < 400, "offset found within 400 ms: " + errMs);
            T.True(Math.Abs(errMs + meanLat) < 120, "the remaining error is the screen latency: " + (errMs + meanLat));
            T.True(conf > 3, "clear peak: " + conf);
        }
        T.Note("offset_estimation", numbers.Trim());
        // no relation between the two lists: low confidence
        var r2 = new Random(9); var x = new List<double>(); var y = new List<double>();
        for (int i = 0; i < 40; i++) { x.Add(r2.NextDouble() * 600); y.Add(r2.NextDouble() * 600); }
        double c2; int m2; ClockMap.EstimateOffset(x, y, 600, 0.25, out c2, out m2);
        T.True(c2 < 2.5, "unrelated lists give no clear peak: " + c2);
    }
}
}
