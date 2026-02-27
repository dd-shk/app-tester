using System;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FlowRunner
{
    public static class CheckpointComparer
    {
        public sealed class Result
        {
            public bool IsMatch { get; set; }
            public int BadSamples { get; set; }
            public int TotalSamples { get; set; }
            public double DiffPercent { get; set; } // درصد (0..100)
            public Bitmap? DiffImage { get; set; }
        }

        public static Result Compare(
            Bitmap expected,
            Bitmap actual,
            int sampleStep = 1,
            int perChannelThreshold = 10,
            double allowedDiffPercent = 0.05,
            int minBadSamples = 25,
            bool generateDiff = false)
        {
            if (expected == null) throw new ArgumentNullException(nameof(expected));
            if (actual == null) throw new ArgumentNullException(nameof(actual));

            using var exp = Ensure32bpp(expected);
            using var act = Ensure32bpp(actual);

            if (exp.Width != act.Width || exp.Height != act.Height)
            {
                return new Result
                {
                    IsMatch = false,
                    BadSamples = int.MaxValue,
                    TotalSamples = int.MaxValue,
                    DiffPercent = 100.0,
                    DiffImage = generateDiff ? CreateSizeMismatchDiff(exp, act) : null
                };
            }

            sampleStep = Math.Max(1, sampleStep);
            perChannelThreshold = Math.Max(0, perChannelThreshold);

            int w = exp.Width;
            int h = exp.Height;

            var rect = new Rectangle(0, 0, w, h);
            var expData = exp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var actData = act.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            Bitmap? diffBmp = null;
            BitmapData? diffData = null;

            try
            {
                int strideE = expData.Stride;
                int strideA = actData.Stride;

                byte[] bufE = new byte[strideE * h];
                byte[] bufA = new byte[strideA * h];

                Marshal.Copy(expData.Scan0, bufE, 0, bufE.Length);
                Marshal.Copy(actData.Scan0, bufA, 0, bufA.Length);

                byte[]? bufD = null;
                int strideD = 0;

                if (generateDiff)
                {
                    diffBmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                    diffData = diffBmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    strideD = diffData.Stride;
                    bufD = new byte[strideD * h];
                    // پیش‌فرض: کاملاً سیاه
                }

                int total = 0;
                int bad = 0;

                for (int y = 0; y < h; y += sampleStep)
                {
                    int rowE = y * strideE;
                    int rowA = y * strideA;
                    int rowD = y * strideD;

                    for (int x = 0; x < w; x += sampleStep)
                    {
                        total++;

                        int iE = rowE + x * 4;
                        int iA = rowA + x * 4;

                        // BGRA
                        int bE = bufE[iE + 0], gE = bufE[iE + 1], rE = bufE[iE + 2];
                        int bA = bufA[iA + 0], gA = bufA[iA + 1], rA = bufA[iA + 2];

                        int db = Math.Abs(bE - bA);
                        int dg = Math.Abs(gE - gA);
                        int dr = Math.Abs(rE - rA);

                        bool isBad = (db > perChannelThreshold) || (dg > perChannelThreshold) || (dr > perChannelThreshold);
                        if (isBad) bad++;

                        if (generateDiff && bufD != null)
                        {
                            // اگر اختلاف داشت: قرمز، اگر نداشت: سیاه
                            int iD = rowD + x * 4;
                            if (isBad)
                            {
                                bufD[iD + 0] = 0;   // B
                                bufD[iD + 1] = 0;   // G
                                bufD[iD + 2] = 255; // R
                                bufD[iD + 3] = 255; // A
                            }
                            else
                            {
                                bufD[iD + 0] = 0;
                                bufD[iD + 1] = 0;
                                bufD[iD + 2] = 0;
                                bufD[iD + 3] = 255;
                            }
                        }
                    }
                }

                double diffPercent = total == 0 ? 0 : (bad * 100.0 / total);

                // ✅ منطق کم‌ریسک: فقط وقتی FAIL واقعی است که هم درصد بیشتر از حد باشد و هم تعداد بدها به حداقل برسد
                bool fail = (diffPercent > allowedDiffPercent) && (bad >= minBadSamples);

                if (generateDiff && diffBmp != null && diffData != null)
                {
                    Marshal.Copy(bufD!, 0, diffData.Scan0, bufD!.Length);
                }

                return new Result
                {
                    IsMatch = !fail,
                    BadSamples = bad,
                    TotalSamples = total,
                    DiffPercent = diffPercent,
                    DiffImage = diffBmp
                };
            }
            finally
            {
                exp.UnlockBits(expData);
                act.UnlockBits(actData);

                if (diffBmp != null && diffData != null)
                    diffBmp.UnlockBits(diffData);
            }
        }

        private static Bitmap Ensure32bpp(Bitmap src)
        {
            if (src.PixelFormat == PixelFormat.Format32bppArgb)
                return (Bitmap)src.Clone();

            var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.DrawImageUnscaled(src, 0, 0);
            return bmp;
        }

        private static Bitmap CreateSizeMismatchDiff(Bitmap a, Bitmap b)
        {
            var w = Math.Max(a.Width, b.Width);
            var h = Math.Max(a.Height, b.Height);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.DarkRed);
            return bmp;
        }
    }
}