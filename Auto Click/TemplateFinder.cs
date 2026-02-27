using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FlowRunner
{
    internal static class TemplateFinder
    {
        // خیلی سبک: Sampling-based matching (0..1)
        // برای UI دکمه‌ها خوبه.
        public static bool ContainsTemplate(Bitmap screen, Bitmap template, double threshold = 0.90, int step = 3)
        {
            if (threshold <= 0) threshold = 0.90;
            if (step < 1) step = 1;

            int sw = screen.Width, sh = screen.Height;
            int tw = template.Width, th = template.Height;
            if (tw <= 0 || th <= 0) return false;
            if (tw > sw || th > sh) return false;

            // به 32bpp تبدیل می‌کنیم برای سرعت/ثبات
            using var s32 = To32bpp(screen);
            using var t32 = To32bpp(template);

            var sRect = new Rectangle(0, 0, sw, sh);
            var tRect = new Rectangle(0, 0, tw, th);

            var sData = s32.LockBits(sRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var tData = t32.LockBits(tRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            try
            {
                int sStride = Math.Abs(sData.Stride);
                int tStride = Math.Abs(tData.Stride);

                byte[] sBytes = new byte[sStride * sh];
                byte[] tBytes = new byte[tStride * th];

                Marshal.Copy(sData.Scan0, sBytes, 0, sBytes.Length);
                Marshal.Copy(tData.Scan0, tBytes, 0, tBytes.Length);

                // تعداد نمونه‌ها
                int samples = 0;
                for (int y = 0; y < th; y += step)
                    for (int x = 0; x < tw; x += step)
                        samples++;

                // پیمایش همه‌ی موقعیت‌های ممکن (با گام 1؛ میشه بعداً گام دارش کرد)
                for (int oy = 0; oy <= sh - th; oy += 1)
                {
                    for (int ox = 0; ox <= sw - tw; ox += 1)
                    {
                        int good = 0;

                        for (int y = 0; y < th; y += step)
                        {
                            int sRow = (oy + y) * sStride;
                            int tRow = y * tStride;

                            for (int x = 0; x < tw; x += step)
                            {
                                int si = sRow + (ox + x) * 4;
                                int ti = tRow + x * 4;

                                // B,G,R
                                int db = Math.Abs(sBytes[si + 0] - tBytes[ti + 0]);
                                int dg = Math.Abs(sBytes[si + 1] - tBytes[ti + 1]);
                                int dr = Math.Abs(sBytes[si + 2] - tBytes[ti + 2]);

                                // tolerance per pixel
                                if (db <= 18 && dg <= 18 && dr <= 18)
                                    good++;
                            }
                        }

                        double score = (double)good / samples;
                        if (score >= threshold)
                            return true;
                    }
                }

                return false;
            }
            finally
            {
                s32.UnlockBits(sData);
                t32.UnlockBits(tData);
            }
        }

        private static Bitmap To32bpp(Bitmap src)
        {
            if (src.PixelFormat == PixelFormat.Format32bppArgb)
                return (Bitmap)src.Clone();

            var bmp = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.DrawImage(src, 0, 0, src.Width, src.Height);
            return bmp;
        }
    }
}