using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LokalneAI.Harness.Rfem
{
    /// <summary>
    /// Minimalny czytnik OLE Compound File (format starszych plików .rf5 i .rs8): tylko odczyt strumieni po ścieżce
    /// („Results_Main_V_2/.HXN000001”). Obsługuje sektory 512 i 4096 B, DIFAT, mini-strumienie (poniżej 4096 B).
    /// Bez paczek, żeby biblioteka działała na .NET Framework 4.8 i .NET 10 bez dodatkowych dll.
    /// </summary>
    public sealed class CompoundFile
    {
        private const uint EndOfChain = 0xFFFFFFFE;
        private const uint FreeSector = 0xFFFFFFFF;
        private static readonly byte[] Signature = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };

        private readonly byte[] _file;
        private readonly int _sectorSize;
        private readonly int _miniSectorSize;
        private readonly uint _miniCutoff;
        private readonly uint[] _fat;
        private readonly uint[] _miniFat;
        private readonly byte[] _miniStream;
        private readonly Dictionary<string, DirectoryEntry> _entries = new Dictionary<string, DirectoryEntry>(StringComparer.OrdinalIgnoreCase);

        private sealed class DirectoryEntry
        {
            public string Name;
            public byte Type;
            public uint Left;
            public uint Right;
            public uint Child;
            public uint StartSector;
            public long Size;
            public string Path;
        }

        /// <summary>Czy plik zaczyna się od sygnatury OLE.</summary>
        public static bool IsCompoundFile(string path)
        {
            using (var stream = File.OpenRead(path))
            {
                var head = new byte[8];
                return stream.Read(head, 0, 8) == 8 && Matches(head);
            }
        }

        /// <summary>Czy bajty zaczynają się od sygnatury OLE.</summary>
        public static bool HasSignature(byte[] bytes) => bytes != null && bytes.Length >= 8 && Matches(bytes);

        private static bool Matches(byte[] head)
        {
            for (var i = 0; i < 8; i++)
            {
                if (head[i] != Signature[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Wczytuje cały plik do pamięci (pliki .rf5 bez wyników mają kilka MB; z wynikami bywają setki MB — wtedy lepiej <see cref="Open"/>).</summary>
        public CompoundFile(byte[] file)
        {
            _file = file ?? throw new ArgumentNullException(nameof(file));
            if (file.Length < 512 || !Matches(file))
            {
                throw new InvalidDataException("To nie jest plik OLE Compound File.");
            }

            var sectorShift = BitConverter.ToUInt16(file, 0x1E);
            var miniShift = BitConverter.ToUInt16(file, 0x20);
            _sectorSize = 1 << sectorShift;
            _miniSectorSize = 1 << miniShift;
            var fatSectorCount = BitConverter.ToUInt32(file, 0x2C);
            var firstDirectory = BitConverter.ToUInt32(file, 0x30);
            _miniCutoff = BitConverter.ToUInt32(file, 0x38);
            var firstMiniFat = BitConverter.ToUInt32(file, 0x3C);
            var miniFatCount = BitConverter.ToUInt32(file, 0x40);
            var firstDifat = BitConverter.ToUInt32(file, 0x44);
            var difatCount = BitConverter.ToUInt32(file, 0x48);

            // DIFAT: 109 wpisów w nagłówku, reszta w sektorach DIFAT.
            var fatSectors = new List<uint>();
            for (var i = 0; i < 109 && fatSectors.Count < fatSectorCount; i++)
            {
                var s = BitConverter.ToUInt32(file, 0x4C + 4 * i);
                if (s != FreeSector)
                {
                    fatSectors.Add(s);
                }
            }

            var difat = firstDifat;
            for (var d = 0; d < difatCount && difat != EndOfChain && difat != FreeSector; d++)
            {
                var offset = SectorOffset(difat);
                var perSector = _sectorSize / 4 - 1;
                for (var i = 0; i < perSector && fatSectors.Count < fatSectorCount; i++)
                {
                    var s = BitConverter.ToUInt32(file, (int)(offset + 4 * i));
                    if (s != FreeSector)
                    {
                        fatSectors.Add(s);
                    }
                }

                difat = BitConverter.ToUInt32(file, (int)(offset + 4 * perSector));
            }

            var entriesPerSector = _sectorSize / 4;
            _fat = new uint[fatSectors.Count * entriesPerSector];
            for (var i = 0; i < fatSectors.Count; i++)
            {
                var offset = SectorOffset(fatSectors[i]);
                for (var j = 0; j < entriesPerSector; j++)
                {
                    _fat[i * entriesPerSector + j] = BitConverter.ToUInt32(file, (int)(offset + 4 * j));
                }
            }

            var miniFatBytes = ReadChain(firstMiniFat, (long)miniFatCount * _sectorSize);
            _miniFat = new uint[miniFatBytes.Length / 4];
            for (var i = 0; i < _miniFat.Length; i++)
            {
                _miniFat[i] = BitConverter.ToUInt32(miniFatBytes, 4 * i);
            }

            var directory = ReadChain(firstDirectory, long.MaxValue);
            var entries = new List<DirectoryEntry>();
            for (var offset = 0; offset + 128 <= directory.Length; offset += 128)
            {
                var nameLength = BitConverter.ToUInt16(directory, offset + 64);
                var entry = new DirectoryEntry
                {
                    Name = nameLength >= 2 ? Encoding.Unicode.GetString(directory, offset, nameLength - 2) : "",
                    Type = directory[offset + 66],
                    Left = BitConverter.ToUInt32(directory, offset + 68),
                    Right = BitConverter.ToUInt32(directory, offset + 72),
                    Child = BitConverter.ToUInt32(directory, offset + 76),
                    StartSector = BitConverter.ToUInt32(directory, offset + 116),
                    Size = sectorShift == 9 ? BitConverter.ToUInt32(directory, offset + 120) : BitConverter.ToInt64(directory, offset + 120),
                };
                entries.Add(entry);
            }

            if (entries.Count == 0 || entries[0].Type != 5)
            {
                throw new InvalidDataException("Plik OLE bez katalogu głównego.");
            }

            _miniStream = ReadChain(entries[0].StartSector, entries[0].Size);
            Walk(entries, entries[0].Child, "");
        }

        /// <summary>Otwiera plik z dysku.</summary>
        public static CompoundFile Open(string path) => new CompoundFile(File.ReadAllBytes(path));

        /// <summary>Ścieżki wszystkich strumieni („Folder/Nazwa”).</summary>
        public IEnumerable<string> Streams => _entries.Keys;

        /// <summary>Czy jest strumień o tej ścieżce.</summary>
        public bool Exists(string path) => _entries.ContainsKey(path);

        /// <summary>Rozmiar strumienia w bajtach (−1 = nie ma).</summary>
        public long Size(string path) => _entries.TryGetValue(path, out var entry) ? entry.Size : -1;

        /// <summary>Zawartość strumienia.</summary>
        public byte[] Read(string path)
        {
            if (!_entries.TryGetValue(path, out var entry))
            {
                throw new FileNotFoundException("W pliku OLE nie ma strumienia " + path, path);
            }

            if (entry.Size == 0)
            {
                return new byte[0];
            }

            return entry.Size < _miniCutoff ? ReadMiniChain(entry.StartSector, entry.Size) : ReadChain(entry.StartSector, entry.Size);
        }

        private void Walk(List<DirectoryEntry> entries, uint index, string prefix)
        {
            var stack = new Stack<uint>();
            stack.Push(index);
            var visited = new HashSet<uint>();
            while (stack.Count > 0)
            {
                var i = stack.Pop();
                if (i == FreeSector || i >= entries.Count || !visited.Add(i))
                {
                    continue;
                }

                var entry = entries[(int)i];
                if (entry.Type == 0)
                {
                    continue;
                }

                stack.Push(entry.Left);
                stack.Push(entry.Right);
                var path = prefix.Length == 0 ? entry.Name : prefix + "/" + entry.Name;
                if (entry.Type == 1)
                {
                    Walk(entries, entry.Child, path);
                }
                else if (entry.Type == 2)
                {
                    entry.Path = path;
                    _entries[path] = entry;
                }
            }
        }

        private long SectorOffset(uint sector) => ((long)sector + 1) * _sectorSize;

        private byte[] ReadChain(uint start, long size)
        {
            using (var output = new MemoryStream())
            {
                var sector = start;
                var guard = 0;
                while (sector != EndOfChain && sector != FreeSector && output.Length < size)
                {
                    if (sector >= _fat.Length || guard++ > _fat.Length)
                    {
                        break; // uszkodzony łańcuch — oddajemy to, co jest
                    }

                    var offset = SectorOffset(sector);
                    var count = (int)Math.Min(_sectorSize, Math.Min(size - output.Length, _file.Length - offset));
                    if (count <= 0)
                    {
                        break;
                    }

                    output.Write(_file, (int)offset, count);
                    sector = _fat[sector];
                }

                return output.ToArray();
            }
        }

        private byte[] ReadMiniChain(uint start, long size)
        {
            using (var output = new MemoryStream())
            {
                var sector = start;
                var guard = 0;
                while (sector != EndOfChain && sector != FreeSector && output.Length < size)
                {
                    if (sector >= _miniFat.Length || guard++ > _miniFat.Length)
                    {
                        break;
                    }

                    var offset = (long)sector * _miniSectorSize;
                    var count = (int)Math.Min(_miniSectorSize, Math.Min(size - output.Length, _miniStream.Length - offset));
                    if (count <= 0)
                    {
                        break;
                    }

                    output.Write(_miniStream, (int)offset, count);
                    sector = _miniFat[sector];
                }

                return output.ToArray();
            }
        }
    }
}
