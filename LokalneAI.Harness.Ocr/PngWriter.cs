using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace LokalneAI.Harness.Ocr
{
    /// <summary>
    /// Minimalny koder PNG (RGB 8 bitów, bez zależności): strona PDF zrenderowana przez PDFium (BGRA) trafia do Tesseracta
    /// jako PNG w pamięci. Bez System.Drawing, bo go nie ma w netstandard2.0, a SkiaSharp to kolejne 10 MB natywnych bibliotek.
    /// </summary>
    internal static class PngWriter
    {
        private static uint[] _crcTable;

        /// <summary>
        /// PNG z surowych pikseli BGRA (4 bajty na piksel, wiersz po wierszu). Kanał alfa jest składany na białym tle: PDFium
        /// renderuje strony z przezroczystym tłem, a czarny tekst na przezroczystym to dla Tesseracta czarne na czarnym.
        /// <paramref name="dpi"/> trafia do pHYs, żeby Tesseract znał rozdzielczość.
        /// </summary>
        public static byte[] FromBgra(byte[] bgra, int width, int height, int dpi)
        {
            var stride = width * 3 + 1;
            var raw = new byte[stride * height];
            var p = 0;
            for (var y = 0; y < height; y++)
            {
                raw[p++] = 0; // filtr: brak
                var row = y * width * 4;
                for (var x = 0; x < width; x++)
                {
                    var i = row + x * 4;
                    int alpha = bgra[i + 3];
                    raw[p++] = Blend(bgra[i + 2], alpha);
                    raw[p++] = Blend(bgra[i + 1], alpha);
                    raw[p++] = Blend(bgra[i], alpha);
                }
            }

            using (var stream = new MemoryStream())
            {
                stream.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
                var header = new byte[13];
                WriteInt(header, 0, width);
                WriteInt(header, 4, height);
                header[8] = 8;  // głębia
                header[9] = 2;  // RGB
                Chunk(stream, "IHDR", header);
                if (dpi > 0)
                {
                    var physical = new byte[9];
                    var perMetre = (int)Math.Round(dpi / 0.0254);
                    WriteInt(physical, 0, perMetre);
                    WriteInt(physical, 4, perMetre);
                    physical[8] = 1;
                    Chunk(stream, "pHYs", physical);
                }

                Chunk(stream, "IDAT", Zlib(raw));
                Chunk(stream, "IEND", new byte[0]);
                return stream.ToArray();
            }
        }

        /// <summary>Kolor złożony na białym tle: alfa 255 = kolor, alfa 0 = biel.</summary>
        private static byte Blend(byte colour, int alpha) => (byte)((colour * alpha + 255 * (255 - alpha)) / 255);

        private static byte[] Zlib(byte[] data)
        {
            using (var stream = new MemoryStream())
            {
                stream.WriteByte(0x78);
                stream.WriteByte(0x9C);
                using (var deflate = new DeflateStream(stream, CompressionLevel.Fastest, true))
                {
                    deflate.Write(data, 0, data.Length);
                }

                uint a = 1, b = 0;
                foreach (var x in data)
                {
                    a = (a + x) % 65521;
                    b = (b + a) % 65521;
                }

                var adler = (b << 16) | a;
                stream.Write(new[] { (byte)(adler >> 24), (byte)(adler >> 16), (byte)(adler >> 8), (byte)adler }, 0, 4);
                return stream.ToArray();
            }
        }

        private static void Chunk(Stream stream, string type, byte[] data)
        {
            var length = new byte[4];
            WriteInt(length, 0, data.Length);
            stream.Write(length, 0, 4);
            var typeBytes = Encoding.ASCII.GetBytes(type);
            stream.Write(typeBytes, 0, 4);
            stream.Write(data, 0, data.Length);
            var crc = new byte[4];
            WriteInt(crc, 0, (int)Crc32(typeBytes, data));
            stream.Write(crc, 0, 4);
        }

        private static void WriteInt(byte[] buffer, int at, int value)
        {
            buffer[at] = (byte)(value >> 24);
            buffer[at + 1] = (byte)(value >> 16);
            buffer[at + 2] = (byte)(value >> 8);
            buffer[at + 3] = (byte)value;
        }

        private static uint Crc32(byte[] first, byte[] second)
        {
            if (_crcTable == null)
            {
                var table = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    var c = n;
                    for (var k = 0; k < 8; k++)
                    {
                        c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                    }

                    table[n] = c;
                }

                _crcTable = table;
            }

            var crc = 0xFFFFFFFF;
            foreach (var x in first)
            {
                crc = _crcTable[(crc ^ x) & 0xFF] ^ (crc >> 8);
            }

            foreach (var x in second)
            {
                crc = _crcTable[(crc ^ x) & 0xFF] ^ (crc >> 8);
            }

            return crc ^ 0xFFFFFFFF;
        }
    }
}
