using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LokalneAI.Harness.Rfem
{
    /// <summary>Plik .rf5 w pamięci: model (tabele), siatka solvera i wyniki (gdy są). Nie trzyma uchwytu do pliku.</summary>
    public sealed class Rf5Document : IDisposable
    {
        private readonly Rf5Archive _archive;

        private Rf5Document(string path, Rf5Archive archive, Rf5Model model, Rf5Mesh mesh, Rf5Results results, DateTime lastWrite, long size)
        {
            Path = path;
            _archive = archive;
            Model = model;
            Mesh = mesh;
            Results = results;
            LastWrite = lastWrite;
            SizeBytes = size;
        }

        /// <summary>Ścieżka pliku.</summary>
        public string Path { get; }

        /// <summary>„ZIP” albo „OLE”.</summary>
        public string Container => _archive.Container;

        /// <summary>Tabele modelu.</summary>
        public Rf5Model Model { get; }

        /// <summary>Siatka solvera (pusta, gdy model nie był liczony).</summary>
        public Rf5Mesh Mesh { get; }

        /// <summary>Wyniki sił w prętach albo null.</summary>
        public Rf5Results Results { get; }

        /// <summary>Czas zapisu pliku przy wczytaniu (do odświeżania pamięci podręcznej).</summary>
        public DateTime LastWrite { get; }

        /// <summary>Rozmiar pliku przy wczytaniu.</summary>
        public long SizeBytes { get; }

        /// <summary>Czy plik ma wyniki sił w prętach.</summary>
        public bool HasResults => Results != null && Results.Sets.Count > 0;

        /// <summary>Wczytuje plik .rf5 (cały do pamięci).</summary>
        public static Rf5Document Open(string path)
        {
            var info = new FileInfo(path);
            var archive = Rf5Archive.Open(path);
            try
            {
                return Load(path, archive, info.Exists ? info.LastWriteTimeUtc : DateTime.MinValue, info.Exists ? info.Length : 0);
            }
            catch
            {
                archive.Dispose();
                throw;
            }
        }

        /// <summary>Wczytuje plik .rf5 z bajtów (ścieżka tylko do komunikatów).</summary>
        public static Rf5Document FromBytes(string path, byte[] bytes)
        {
            var archive = Rf5Archive.FromBytes(path, bytes);
            try
            {
                return Load(path, archive, DateTime.MinValue, bytes.Length);
            }
            catch
            {
                archive.Dispose();
                throw;
            }
        }

        private static Rf5Document Load(string path, Rf5Archive archive, DateTime lastWrite, long size)
        {
            if (!archive.Exists(archive.InputDataName))
            {
                throw new RfemException("BŁĄD: " + System.IO.Path.GetFileName(path) + " nie ma strumienia z modelem (" + archive.InputDataName + ") — to nie jest model RFEM 5.");
            }

            var model = Rf5Parser.Parse(archive.ReadInputData());
            if (archive.Exists("History.bin"))
            {
                model.Version = Rf5Parser.ReadVersion(archive.Read("History.bin"));
            }

            var mesh = Rf5Mesh.Read(archive);
            Rf5Results results = null;
            try
            {
                results = Rf5Results.Read(archive, mesh);
            }
            catch (RfemException e)
            {
                model.Warnings.Add("wyniki nieczytelne: " + e.Message);
            }

            return new Rf5Document(path, archive, model, mesh, results, lastWrite, size);
        }

        /// <summary>Długość pręta [m]: z tabel modelu, a gdy ich brak — z siatki solvera.</summary>
        public double? MemberLength(int memberNo)
        {
            if (Model.Members.TryGetValue(memberNo, out var member))
            {
                var fromModel = Model.MemberLength(member);
                if (fromModel.HasValue)
                {
                    return fromModel;
                }
            }

            return Mesh.MemberLength(memberNo);
        }

        /// <summary>Czy plik na dysku zmienił się od wczytania.</summary>
        public bool IsStale()
        {
            var info = new FileInfo(Path);
            return !info.Exists || info.LastWriteTimeUtc != LastWrite || info.Length != SizeBytes;
        }

        /// <inheritdoc/>
        public void Dispose() => _archive.Dispose();
    }
}
