using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace VoxIA.Services
{
    public class DictionaryEntry
    {
        public string Id { get; set; }
        public string Original { get; set; }
        public string Replacement { get; set; }
        public bool IsVocabularyOnly { get; set; }
        public string CreatedAt { get; set; }

        public DictionaryEntry()
        {
            Id = Guid.NewGuid().ToString("N");
            CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public DictionaryEntry(string original, string replacement, bool isVocabularyOnly = false) : this()
        {
            Original = (original ?? "").Trim();
            Replacement = (replacement ?? "").Trim();
            IsVocabularyOnly = isVocabularyOnly;
        }
    }

    public static class DictionaryService
    {
        private static List<DictionaryEntry> _entries = new List<DictionaryEntry>();
        private static readonly object _lock = new object();
        private static bool _isLoaded = false;

        public static event Action OnDictionaryChanged;

        static DictionaryService()
        {
            Load();
        }

        public static void Load()
        {
            lock (_lock)
            {
                try
                {
                    string path = AppPathService.DictionaryFilePath;
                    if (File.Exists(path))
                    {
                        string json = File.ReadAllText(path);
                        var serializer = new JavaScriptSerializer();
                        var list = serializer.Deserialize<List<DictionaryEntry>>(json);
                        _entries = list ?? new List<DictionaryEntry>();
                        Logger.LogInfo(string.Format("Dictionary loaded successfully ({0} entries).", _entries.Count));
                    }
                    else
                    {
                        _entries = new List<DictionaryEntry>();
                        // Seed default rule
                        AddDefaultEntries();
                        SaveInternal();
                    }
                    _isLoaded = true;
                }
                catch (Exception ex)
                {
                    Logger.LogError("Failed to load dictionary.json", ex);
                    _entries = new List<DictionaryEntry>();
                }
            }
        }

        private static void AddDefaultEntries()
        {
            _entries.Add(new DictionaryEntry("desartria", "disartria"));
        }

        public static void Save()
        {
            lock (_lock)
            {
                SaveInternal();
            }
        }

        private static void SaveInternal()
        {
            try
            {
                string path = AppPathService.DictionaryFilePath;
                var serializer = new JavaScriptSerializer();
                string json = serializer.Serialize(_entries);
                File.WriteAllText(path, json);
                Logger.LogInfo("Dictionary saved successfully.");

                if (OnDictionaryChanged != null)
                {
                    try { OnDictionaryChanged(); } catch { }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to save dictionary.json", ex);
            }
        }

        public static List<DictionaryEntry> GetEntries()
        {
            lock (_lock)
            {
                if (!_isLoaded) Load();
                return new List<DictionaryEntry>(_entries);
            }
        }

        public static void SetEntries(List<DictionaryEntry> newEntries)
        {
            lock (_lock)
            {
                _entries = newEntries ?? new List<DictionaryEntry>();
                SaveInternal();
            }
        }


        public static DictionaryEntry AddOrUpdate(string original, string replacement, bool isVocabularyOnly = false)
        {
            if (string.IsNullOrWhiteSpace(original) && string.IsNullOrWhiteSpace(replacement))
                return null;

            string cleanOrig = (original ?? "").Trim();
            string cleanRepl = (replacement ?? "").Trim();

            if (string.IsNullOrEmpty(cleanOrig))
                cleanOrig = cleanRepl;
            if (string.IsNullOrEmpty(cleanRepl))
                cleanRepl = cleanOrig;

            lock (_lock)
            {
                // Check if entry already exists for this original trigger (case-insensitive)
                var existing = _entries.Find(e => string.Equals(e.Original, cleanOrig, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.Replacement = cleanRepl;
                    existing.IsVocabularyOnly = isVocabularyOnly;
                    existing.CreatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    SaveInternal();
                    Logger.LogInfo(string.Format("Dictionary updated: '{0}' -> '{1}'", cleanOrig, cleanRepl));
                    return existing;
                }
                else
                {
                    var newEntry = new DictionaryEntry(cleanOrig, cleanRepl, isVocabularyOnly);
                    _entries.Insert(0, newEntry);
                    SaveInternal();
                    Logger.LogInfo(string.Format("Dictionary added: '{0}' -> '{1}'", cleanOrig, cleanRepl));
                    return newEntry;
                }
            }
        }

        public static bool Remove(string id)
        {
            lock (_lock)
            {
                int count = _entries.RemoveAll(e => e.Id == id);
                if (count > 0)
                {
                    SaveInternal();
                    Logger.LogInfo(string.Format("Dictionary entry removed: {0}", id));
                    return true;
                }
                return false;
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
                SaveInternal();
                Logger.LogInfo("Dictionary cleared.");
            }
        }

        /// <summary>
        /// Applies deterministic local regex replacement with intelligent Case-Matching across the text.
        /// Guaranteed execution in <1ms without token costs.
        /// </summary>
        public static string ApplyReplacements(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            List<DictionaryEntry> list;
            lock (_lock)
            {
                if (!_isLoaded) Load();
                list = new List<DictionaryEntry>(_entries);
            }

            if (list == null || list.Count == 0) return text;

            string result = text;

            foreach (var entry in list)
            {
                if (entry.IsVocabularyOnly) continue;
                if (string.IsNullOrWhiteSpace(entry.Original) || string.IsNullOrWhiteSpace(entry.Replacement)) continue;

                // If original equals replacement, nothing to swap
                if (string.Equals(entry.Original, entry.Replacement, StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    string pattern = @"\b" + Regex.Escape(entry.Original) + @"\b";
                    result = Regex.Replace(result, pattern, match =>
                    {
                        return MatchCase(match.Value, entry.Replacement);
                    }, RegexOptions.IgnoreCase);
                }
                catch (Exception ex)
                {
                    Logger.LogError(string.Format("Error applying dictionary rule '{0}' -> '{1}'", entry.Original, entry.Replacement), ex);
                }
            }

            return result;
        }

        /// <summary>
        /// Preserves the casing pattern of the original matched word.
        /// </summary>
        private static string MatchCase(string originalMatch, string targetReplacement)
        {
            if (string.IsNullOrEmpty(originalMatch) || string.IsNullOrEmpty(targetReplacement))
                return targetReplacement;

            // 1. All Uppercase: "DESARTRIA" -> "DISARTRIA"
            if (IsAllUpper(originalMatch))
            {
                return targetReplacement.ToUpper();
            }

            // 2. Title / Capitalized: "Desartria" -> "Disartria"
            if (char.IsUpper(originalMatch[0]))
            {
                if (targetReplacement.Length == 1)
                    return targetReplacement.ToUpper();
                return char.ToUpper(targetReplacement[0]) + targetReplacement.Substring(1);
            }

            // 3. Lowercase / Default: "desartria" -> "disartria"
            return targetReplacement;
        }

        private static bool IsAllUpper(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            bool hasLetters = false;
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsLetter(s[i]))
                {
                    hasLetters = true;
                    if (!char.IsUpper(s[i]))
                        return false;
                }
            }
            return hasLetters;
        }
    }
}
