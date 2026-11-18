using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace LokalneAI.Harness.Rfem
{
    /// <summary>Błąd czytania pliku RFEM z komunikatem dla modelu (po polsku).</summary>
    public sealed class RfemException : Exception
    {
        /// <summary>Tworzy błąd z komunikatem dla modelu.</summary>
        public RfemException(string message) : base(message)
        {
        }

        /// <summary>Tworzy błąd z komunikatem i przyczyną.</summary>
        public RfemException(string message, Exception inner) : base(message, inner)
        {
        }
    }

    /// <summary>
    /// Kontener pliku .rf5: ZIP albo OLE Compound File (RFEM 5.27 zapisuje oba — zależnie od ustawień i rozmiaru). Oba mają te same
    /// strumienie: model (<c>RFEM Input Data.bin</c> w ZIP, <c>RFEM_Data 2.002</c> w OLE), siatkę <c>Mesh/…</c> i wyniki
    /// <c>Results_Main_V_2/…</c> (tylko po obliczeniu w RFEM). Nazwy strumieni są podawane ze slashem. Cały plik jest czytany
    /// do pamięci, żeby nie trzymać uchwytu — RFEM może w tym czasie zapisywać ten sam plik.
    /// </summary>
    public sealed class Rf5Archive : IDisposable
    {
        /// <summary>Największy plik, który czytamy do pamięci (pliki z wynikami dużych modeli bywają większe).</summary>
        public const long MaxBytes = 600L * 1024 * 1024;

        private readonly ZipArchive _zip;
        private readonly CompoundFile _ole;
        private readonly Dictionary<string, ZipArchiveEntry> _zipEntries;

        private Rf5Archive(string path, ZipArchive zip, CompoundFile ole)
        {
            Path = path;
            _zip = zip;
            _ole = ole;
            if (zip != null)
            {
                _zipEntries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries)
                {
                    _zipEntries[entry.FullName.Replace('\\', '/')] = entry;
                }
            }
        }

        /// <summary>Ścieżka pliku.</summary>
        public string Path { get; }

        /// <summary>„ZIP” albo „OLE”.</summary>
        public string Container => _zip != null ? "ZIP" : "OLE";

        /// <summary>Nazwa strumienia z modelem (różna w obu kontenerach).</summary>
        public string InputDataName => _zip != null ? "RFEM Input Data.bin" : "RFEM_Data 2.002";

        /// <summary>Otwiera plik .rf5 (rozpoznaje kontener po sygnaturze).</summary>
        public static Rf5Archive Open(string path)
        {
            if (!File.Exists(path))
            {
                throw new RfemException("BŁĄD: nie ma pliku " + path);
            }

            var info = new FileInfo(path);
            if (info.Length > MaxBytes)
            {
                throw new RfemException("BŁĄD: plik " + info.Name + " ma " + (info.Length / 1024 / 1024) + " MB — za dużo, żeby go wczytać (granica " + (MaxBytes / 1024 / 1024) + " MB). Zapisz w RFEM kopię bez wyników.");
            }

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                throw new RfemException("BŁĄD: nie udało się odczytać " + info.Name + ": " + e.Message, e);
            }

            return FromBytes(path, bytes);
        }

        /// <summary>Otwiera plik .rf5 z pamięci (ścieżka tylko do komunikatów).</summary>
        public static Rf5Archive FromBytes(string path, byte[] bytes)
        {
            if (bytes == null || bytes.Length < 8)
            {
                throw new RfemException("BŁĄD: plik " + System.IO.Path.GetFileName(path) + " jest za krótki na plik RFEM.");
            }

            try
            {
                if (bytes[0] == 0x50 && bytes[1] == 0x4B)
                {
                    return new Rf5Archive(path, new ZipArchive(new MemoryStream(bytes, false), ZipArchiveMode.Read), null);
                }

                if (CompoundFile.HasSignature(bytes))
                {
                    return new Rf5Archive(path, null, new CompoundFile(bytes));
                }
            }
            catch (Exception e) when (e is InvalidDataException || e is IOException || e is NotSupportedException)
            {
                throw new RfemException("BŁĄD: nie udało się otworzyć " + System.IO.Path.GetFileName(path) + " jako pliku RFEM 5: " + e.Message, e);
            }

            throw new RfemException("BŁĄD: " + System.IO.Path.GetFileName(path) + " nie jest plikiem RFEM 5 (ani ZIP, ani OLE). Pliki RFEM 6 (.rf6) mają inną budowę.");
        }

        /// <summary>Wszystkie strumienie w pliku.</summary>
        public IEnumerable<string> Streams => _zip != null ? _zipEntries.Keys : _ole.Streams;

        /// <summary>Czy jest strumień o tej nazwie.</summary>
        public bool Exists(string name) => _zip != null ? _zipEntries.ContainsKey(name) : _ole.Exists(name);

        /// <summary>Rozmiar strumienia; −1 = nie ma.</summary>
        public long Size(string name)
        {
            if (_zip != null)
            {
                return _zipEntries.TryGetValue(name, out var entry) ? entry.Length : -1;
            }

            return _ole.Size(name);
        }

        /// <summary>Zawartość strumienia.</summary>
        public byte[] Read(string name)
        {
            if (_zip != null)
            {
                if (!_zipEntries.TryGetValue(name, out var entry))
                {
                    throw new RfemException("BŁĄD: w pliku nie ma strumienia " + name);
                }

                using (var stream = entry.Open())
                using (var memory = new MemoryStream())
                {
                    stream.CopyTo(memory);
                    return memory.ToArray();
                }
            }

            try
            {
                return _ole.Read(name);
            }
            catch (FileNotFoundException)
            {
                throw new RfemException("BŁĄD: w pliku nie ma strumienia " + name);
            }
        }

        /// <summary>Strumień jako tekst (Latin-1 — pliki siatki i protokoły solvera są w ASCII).</summary>
        public string ReadText(string name) => System.Text.Encoding.GetEncoding("iso-8859-1").GetString(Read(name));

        /// <summary>Strumienie w folderze (np. „Results_Main_V_2”), same nazwy plików.</summary>
        public IList<string> List(string folder)
        {
            var prefix = folder.TrimEnd('/') + "/";
            return Streams.Where(s => s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(s => s.Substring(prefix.Length)).OrderBy(s => s, StringComparer.Ordinal).ToList();
        }

        /// <summary>Model (strumień InputData).</summary>
        public byte[] ReadInputData() => Read(InputDataName);

        /// <inheritdoc/>
        public void Dispose() => _zip?.Dispose();
    }
}
