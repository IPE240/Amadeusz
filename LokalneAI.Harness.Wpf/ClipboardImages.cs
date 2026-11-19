using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LokalneAI.Harness.Wpf
{
    /// <summary>
    /// Obrazy ze schowka i dla modelu: odczyt PNG (najpierw format „PNG”, potem bitmapa), naprawa zrzutów z zerową
    /// przezroczystością, zmniejszanie przed wysłaniem do modelu. Wołać w wątku okna (schowek jest STA).
    /// </summary>
    public static class ClipboardImages
    {
        /// <summary>Dłuższy bok obrazu dla modelu: Claude i GPT i tak zmniejszają większe, a Ollama liczy tokeny od rozmiaru.</summary>
        public const int ModelMaxSide = 1568;

        /// <summary>Czy wklejenie to obrazek (a nie tekst — tekst z obrazkiem, np. z Worda, wkleja się jak tekst).</summary>
        public static bool IsImage(IDataObject data)
            => data != null && !data.GetDataPresent(DataFormats.UnicodeText)
               && (data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent("PNG"));

        /// <summary>Obraz ze schowka jako PNG albo null, gdy go nie ma albo nie da się go odczytać.</summary>
        public static byte[] ReadPng()
        {
            try
            {
                var data = Clipboard.GetDataObject();
                if (data != null && data.GetDataPresent("PNG") && data.GetData("PNG") is MemoryStream stream)
                {
                    return stream.ToArray(); // przeglądarki i Wycinek zapisują PNG z poprawną przezroczystością
                }

                var image = Clipboard.GetImage();
                return image == null ? null : ToPng(Opaque(image));
            }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException || e is NotSupportedException
                || e is InvalidOperationException || e is Win32Exception || e is IOException)
            {
                return null;
            }
        }

        /// <summary>Zapisuje obraz jako PNG.</summary>
        public static byte[] ToPng(BitmapSource image)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }

        /// <summary>Wczytuje PNG albo JPEG do pamięci (zamrożony, bez trzymania strumienia).</summary>
        public static BitmapSource Load(byte[] image, int decodeWidth = 0)
        {
            var bitmap = new BitmapImage();
            using (var stream = new MemoryStream(image))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                if (decodeWidth > 0)
                {
                    bitmap.DecodePixelWidth = decodeWidth;
                }

                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }

            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>Zmniejsza obraz tak, by dłuższy bok miał najwyżej <paramref name="maxSide"/> pikseli; mniejszy zwraca bez zmian.</summary>
        public static byte[] Shrink(byte[] png, int maxSide = ModelMaxSide)
        {
            var source = Load(png);
            var longer = Math.Max(source.PixelWidth, source.PixelHeight);
            if (longer <= maxSide)
            {
                return png;
            }

            var scale = (double)maxSide / longer;
            var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            scaled.Freeze();
            return ToPng(scaled);
        }

        /// <summary>
        /// Zrzut ekranu ze schowka bywa w formacie z kanałem alfa równym zero (CF_DIB) — wtedy obraz byłby niewidoczny;
        /// taki obraz zamieniamy na nieprzezroczysty. Obraz z prawdziwą przezroczystością zostaje.
        /// </summary>
        private static BitmapSource Opaque(BitmapSource image)
        {
            if (image.Format != PixelFormats.Bgra32 && image.Format != PixelFormats.Pbgra32)
            {
                return image;
            }

            var stride = image.PixelWidth * 4;
            var pixels = new byte[stride * image.PixelHeight];
            image.CopyPixels(pixels, stride, 0);
            for (var i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0)
                {
                    return image;
                }
            }

            var opaque = new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0);
            opaque.Freeze();
            return opaque;
        }
    }

    /// <summary>Obrazek wklejony do pola polecenia, który pójdzie do modelu (miniatura nad polem, „×” go usuwa).</summary>
    public sealed class PendingImage
    {
        /// <summary>Tworzy pozycję z PNG (już zmniejszonym dla modelu).</summary>
        public PendingImage(byte[] png, string name)
        {
            Png = png;
            Name = name;
            Thumbnail = ClipboardImages.Load(png, 96);
        }

        /// <summary>Obraz dla modelu (PNG).</summary>
        public byte[] Png { get; }

        /// <summary>Nazwa do podpisu, np. „obraz 1”.</summary>
        public string Name { get; }

        /// <summary>Miniatura nad polem polecenia.</summary>
        public BitmapSource Thumbnail { get; }
    }
}
