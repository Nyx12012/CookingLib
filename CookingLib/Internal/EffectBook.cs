using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MelonLoader.Utils;

namespace CookingLib
{
    internal static class EffectBook
    {
        internal const string FileName = "CookingLib-effects.txt";

        static Dictionary<uint, Entry> entries;

        internal static string Path => System.IO.Path.Combine(MelonEnvironment.UserDataDirectory, FileName);

        internal static IReadOnlyCollection<Entry> All
        {
            get
            {
                Load();
                return entries.Values;
            }
        }

        internal static string Owner(uint number)
        {
            Load();
            return entries.TryGetValue(number, out Entry entry) ? entry.Id : null;
        }

        internal static void Book(uint number, string id, string kind)
        {
            Load();
            if (entries.TryGetValue(number, out Entry known) && string.Equals(known.Id, id, StringComparison.OrdinalIgnoreCase))
                return;

            entries[number] = new Entry(number, id, kind);
            File.AppendAllText(Path, number.ToString(CultureInfo.InvariantCulture) + "\t" + id + "\t" + kind + Environment.NewLine);
        }

        static void Load()
        {
            if (entries != null)
                return;

            entries = new Dictionary<uint, Entry>();
            if (!File.Exists(Path))
                return;

            foreach (string line in File.ReadAllLines(Path))
            {
                string[] parts = line.Split('\t');
                if (parts.Length < 2 || !uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out uint number) || parts[1].Trim().Length == 0)
                    continue;
                if (!entries.ContainsKey(number))
                    entries[number] = new Entry(number, parts[1].Trim(), parts.Length > 2 ? parts[2].Trim() : "");
            }
        }

        internal static void ForgetCache() => entries = null;

        internal static IEnumerable<Entry> Missing(IEnumerable<uint> registered)
        {
            HashSet<uint> live = new HashSet<uint>(registered);
            return All.Where(e => !live.Contains(e.Number)).OrderBy(e => e.Number).ToList();
        }

        internal sealed class Entry
        {
            internal Entry(uint number, string id, string kind)
            {
                Number = number;
                Id = id;
                Kind = kind;
            }

            internal uint Number { get; }

            internal string Id { get; }

            internal string Kind { get; }
        }
    }
}
