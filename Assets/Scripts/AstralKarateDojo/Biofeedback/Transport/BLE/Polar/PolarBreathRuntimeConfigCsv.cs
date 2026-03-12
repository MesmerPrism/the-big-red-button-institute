using System;
using System.Collections.Generic;
using System.IO;

namespace AstralKarateDojo.Biofeedback.Transport.BLE.Polar
{
    public readonly struct PolarRuntimeConfigEntry
    {
        public PolarRuntimeConfigEntry(string key, string value, int lineNumber)
        {
            Key = key;
            Value = value;
            LineNumber = lineNumber;
        }

        public string Key { get; }
        public string Value { get; }
        public int LineNumber { get; }
    }

    public static class PolarBreathRuntimeConfigCsv
    {
        public const string DefaultRelativePath = "runtime_config/breath_runtime_config.csv";

        public static bool TryReadEntries(string path, out List<PolarRuntimeConfigEntry> entries, out string error)
        {
            entries = new List<PolarRuntimeConfigEntry>();
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Path is empty.";
                return false;
            }

            if (!File.Exists(path))
            {
                error = $"File not found: {path}";
                return false;
            }

            string csvText;
            try
            {
                csvText = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                error = $"Failed reading file '{path}': {ex.Message}";
                return false;
            }

            return TryParseEntries(csvText, out entries, out error);
        }

        public static bool TryParseEntries(string csvText, out List<PolarRuntimeConfigEntry> entries, out string error)
        {
            entries = new List<PolarRuntimeConfigEntry>();
            error = string.Empty;

            if (csvText == null)
            {
                error = "CSV text is null.";
                return false;
            }

            using StringReader reader = new StringReader(csvText);
            string rawLine;
            int lineNumber = 0;
            bool headerConsumed = false;

            while ((rawLine = reader.ReadLine()) != null)
            {
                lineNumber++;
                string trimmed = rawLine.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#") || trimmed.StartsWith("//"))
                    continue;

                int commaIndex = rawLine.IndexOf(',');
                if (commaIndex < 0)
                {
                    error = $"Line {lineNumber} is not valid 'key,value' CSV: '{rawLine}'.";
                    entries.Clear();
                    return false;
                }

                string key = rawLine.Substring(0, commaIndex).Trim();
                string value = rawLine.Substring(commaIndex + 1).Trim();

                if (!headerConsumed && IsHeader(key, value))
                {
                    headerConsumed = true;
                    continue;
                }

                headerConsumed = true;

                if (key.Length == 0)
                {
                    error = $"Line {lineNumber} has an empty key.";
                    entries.Clear();
                    return false;
                }

                if (value.Length == 0)
                {
                    error = $"Line {lineNumber} has an empty value for key '{key}'.";
                    entries.Clear();
                    return false;
                }

                entries.Add(new PolarRuntimeConfigEntry(key, value, lineNumber));
            }

            return true;
        }

        private static bool IsHeader(string key, string value)
        {
            return key.Equals("key", StringComparison.OrdinalIgnoreCase) &&
                   value.Equals("value", StringComparison.OrdinalIgnoreCase);
        }
    }
}

