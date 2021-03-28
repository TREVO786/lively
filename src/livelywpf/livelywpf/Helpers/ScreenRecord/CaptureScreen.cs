﻿using ImageMagick;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace livelywpf
{
    public static class CaptureScreen
    {
        /// <summary>
        /// Captures screen foreground image.
        /// </summary>
        /// <param name="savePath"></param>
        /// <param name="fileName"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        public static void CopyScreen(string savePath, string fileName, int x, int y, int width, int height)
        {
            using (var screenBmp = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                using (var bmpGraphics = Graphics.FromImage(screenBmp))
                {
                    bmpGraphics.CopyFromScreen(x, y, 0, 0, screenBmp.Size);
                    screenBmp.Save(Path.Combine(savePath, fileName), ImageFormat.Jpeg);
                }
            }
        }

        public static Bitmap CopyScreen(int x, int y, int width, int height)
        {
            var screenBmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var bmpGraphics = Graphics.FromImage(screenBmp))
            {
                bmpGraphics.CopyFromScreen(x, y, 0, 0, screenBmp.Size);
                return screenBmp;
            }
        }

        /// <summary>
        /// Capture window, can work if not foreground.
        /// </summary>
        /// <param name="hWnd">Window handle</param>
        /// <returns></returns>
        public static Bitmap CaptureWindow(IntPtr hWnd)
        {
            NativeMethods.GetWindowRect(hWnd, out NativeMethods.RECT rect);
            var region = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);

            IntPtr winDc;
            IntPtr memoryDc;
            IntPtr bitmap;
            IntPtr oldBitmap;
            bool success;
            Bitmap result;

            winDc = NativeMethods.GetWindowDC(hWnd);
            memoryDc = NativeMethods.CreateCompatibleDC(winDc);
            bitmap = NativeMethods.CreateCompatibleBitmap(winDc, region.Width, region.Height);
            oldBitmap = NativeMethods.SelectObject(memoryDc, bitmap);

            success = NativeMethods.BitBlt(memoryDc, 0, 0, region.Width, region.Height, winDc, region.Left, region.Top, 
                NativeMethods.TernaryRasterOperations.SRCCOPY | NativeMethods.TernaryRasterOperations.CAPTUREBLT);

            try
            {
                if (!success)
                {
                    throw new Win32Exception();
                }

                result = Image.FromHbitmap(bitmap);
            }
            finally
            {
                NativeMethods.SelectObject(memoryDc, oldBitmap);
                NativeMethods.DeleteObject(bitmap);
                NativeMethods.DeleteDC(memoryDc);
                NativeMethods.ReleaseDC(hWnd, winDc);
            }
            return result;
        }
        
        public static async Task CaptureGif(string savePath, int x, int y, int width, int height, int captureDelay, int animeDelay, int totalFrames, IProgress<int> progress)
        {
            var miArray = new MagickImage[totalFrames];
            for (int i = 0; i < totalFrames; i++)
            {
                using(var bmp = CopyScreen(x, y, width, height))
                {
                    miArray[i] = ToMagickImage(bmp);
                }
                await Task.Delay(captureDelay);
                progress.Report((i * 100 / totalFrames));
            }

            using (MagickImageCollection collection = new MagickImageCollection())
            {
                for (int i = 0; i < totalFrames; i++)
                {
                    collection.Add(miArray[i]);
                    collection[i].AnimationDelay = animeDelay;
                }

                // Optionally reduce colors
                QuantizeSettings settings = new QuantizeSettings
                {
                    Colors = 256,
                };
                collection.Quantize(settings);

                // Optionally optimize the images (images should have the same size).
                collection.Optimize();

                collection.Write(savePath);
            }

            foreach (var mi in miArray)
            {
                mi.Dispose();
            }
        }

        #region helpers

        public static MagickImage ToMagickImage(Bitmap bmp)
        {
            MagickImage img = null;
            MagickFactory f = new MagickFactory();
            using (MemoryStream ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Bmp);
                ms.Position = 0;
                img = new MagickImage(f.Image.Create(ms));
            }
            return img;
        }

        #endregion //helpers
    }
}
