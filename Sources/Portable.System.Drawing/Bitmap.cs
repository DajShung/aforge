﻿// AForge Image Processing Library
// AForge.NET framework
// http://www.aforgenet.com/framework/
//
// Portable Adaptations
//
// Copyright © Cureos AB, 2013
// info at cureos dot com
//

using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using ImagePixelEnumerator.Extensions;
using ImagePixelEnumerator.Helpers;
using ImagePixelEnumerator.Quantizers;
using ImagePixelEnumerator.Quantizers.DistinctCompetition;

#if NETFX_CORE
using Windows.UI.Xaml.Media.Imaging;
#else
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
#endif

namespace System.Drawing
{
    public sealed class Bitmap : Image, IDisposable
    {
        #region FIELDS

        private static readonly IColorQuantizer Quantizer;

        private bool _disposed = false;

        private readonly int _width;
        private readonly int _height;
        private readonly int _stride;
        private readonly PixelFormat _pixelFormat;

        private readonly IntPtr _scan0;
        private readonly bool _freeScan0;

        #endregion

        #region CONSTRUCTORS

        static Bitmap()
        {
            Quantizer = new DistinctSelectionQuantizer();
        }

        public Bitmap(int width, int height, PixelFormat pixelFormat)
        {
            _width = width;
            _height = height;
            _stride = GetStride(width, pixelFormat);
            _pixelFormat = pixelFormat;

            _scan0 = Marshal.AllocHGlobal(_stride * height);
            _freeScan0 = true;

            if (pixelFormat.IsIndexed())
                Palette = new ColorPalette(new Color[pixelFormat.GetColorCount()]);
        }

        public Bitmap(int width, int height, int stride, PixelFormat pixelFormat, IntPtr scan0)
        {
            _width = width;
            _height = height;
            _stride = stride;
            _pixelFormat = pixelFormat;

            _scan0 = scan0;
            _freeScan0 = false;

            if (pixelFormat.IsIndexed())
                Palette = new ColorPalette(new Color[pixelFormat.GetColorCount()]);
        }

        ~Bitmap()
        {
            Dispose(false);
        }

        #endregion

        #region PROPERTIES

        public override PixelFormat PixelFormat
        {
            get { return _pixelFormat; }
        }

        public override int Width
        {
            get { return _width; }
        }

        public override int Height
        {
            get { return _height; }
        }

        public override ColorPalette Palette { get; set; }

        public int HorizontalResolution { get; private set; }

        public int VerticalResolution { get; private set; }
        
        #endregion

        #region METHODS

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public Bitmap Clone(PixelFormat pixelFormat)
        {
            List<Color> palette = null;

            // indexed formats require 2 passes - one more pass to determines colors for palette beforehand
            if (pixelFormat.IsIndexed())
            {
                Quantizer.Prepare(this);

                // Pass: scan
                ImageBuffer.ProcessPerPixel(this, null, 4, (passIndex, pixel) =>
                {
                    var color = pixel.GetColor();
                    Quantizer.AddColor(color, pixel.X, pixel.Y);
                    return true;
                });

                // determines palette
                palette = Quantizer.GetPalette(pixelFormat.GetColorCount());
            }

            // Pass: apply
            Image result;
            ImageBuffer.TransformImagePerPixel(this, pixelFormat, palette, out result, null, 4, (passIndex, sourcePixel, targetPixel) =>
            {
                var color = sourcePixel.GetColor();
                targetPixel.SetColor(color, Quantizer);
                return true;
            });

            return (Bitmap)result;
        }

        public BitmapData LockBits(Rectangle rectangle, ImageLockMode readOnly, PixelFormat pixelFormat)
        {
            switch (pixelFormat)
            {
                case PixelFormat.Alpha:
                case PixelFormat.PAlpha:
                case PixelFormat.Indexed:
                case PixelFormat.Undefined:
                    throw new ArgumentException("LockBits method only applicable to pixel formats with prefix Format",
                        "pixelFormat");
            }
                
            if (!pixelFormat.Equals(_pixelFormat))
                throw new ArgumentException(String.Format("Bitmap.PixelFormat = {0}", _pixelFormat), "pixelFormat");

            return new BitmapData(_width, _height, _stride, _pixelFormat, _scan0);
        }

        public void UnlockBits(BitmapData sourceData)
        {
            // TODO Need to do anything here?
        }

        public static Bitmap FromStream(Stream stream)
        {
            var writeableBitmap = BitmapFactory.New(1, 1);
#if NETFX_CORE
            return writeableBitmap.FromStream(stream).Result;
#else
            return writeableBitmap.FromStream(stream);
#endif
        }

        public void SetResolution(int horizontalResolution, int verticalResolution)
        {
            HorizontalResolution = horizontalResolution;
            VerticalResolution = verticalResolution;
        }

        private void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // Free managed disposable objects
            }

            // Free unmanaged objects
            if (_freeScan0) Marshal.FreeHGlobal(_scan0);

            _disposed = true;
        }

        private static int GetStride(int width, PixelFormat format)
        {
            var bitsPerPixel = ((int)format & 0xff00) >> 8;
            var bytesPerPixel = (bitsPerPixel + 7) / 8;
            return 4 * ((width * bytesPerPixel + 3) / 4);
        }

        #endregion

        #region OPERATORS

        public static implicit operator WriteableBitmap(Bitmap bitmap)
        {
            if (bitmap.PixelFormat != PixelFormat.Format32bppArgb)
                bitmap = bitmap.Clone(PixelFormat.Format32bppArgb);

            var bytes = new byte[bitmap._stride * bitmap._height];
            Marshal.Copy(bitmap._scan0, bytes, 0, bytes.Length);

            var writeableBitmap = BitmapFactory.New(bitmap._width, bitmap._height);
            return writeableBitmap.FromByteArray(bytes);
        }

        public static implicit operator Bitmap(WriteableBitmap writeableBitmap)
        {
#if !NETFX_CORE && !WINDOWS_PHONE
            if (writeableBitmap.Format != PixelFormats.Pbgra32)
                writeableBitmap = BitmapFactory.ConvertToPbgra32Format(writeableBitmap);
#endif
            var width = writeableBitmap.PixelWidth;
            var height = writeableBitmap.PixelHeight;
            const PixelFormat format = PixelFormat.Format32bppArgb;
            var bytes = writeableBitmap.ToByteArray();

            var bitmap = new Bitmap(width, height, format);
            var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadWrite, format);
            Marshal.Copy(bytes, 0, data.Scan0, bytes.Length);
            bitmap.UnlockBits(data);

            return bitmap;
        }

        #endregion
    }
}
