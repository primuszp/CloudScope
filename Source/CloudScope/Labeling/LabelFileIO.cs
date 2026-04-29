using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CloudScope.Labeling
{
    /// <summary>
    /// Reads and writes label data as JSON.
    /// Format: { "sourceFile": "...", "labels": { "Ground": [0,1,2], "Building": [100,101] } }
    /// </summary>
    public static class LabelFileIO
    {
        /// <summary>
        /// Save all labels to a JSON file next to the source LAS.
        /// </summary>
        public static void Save(string lasFilePath, LabelManager manager)
        {
            // Invert: point→label  →  label→points[]
            var grouped = new Dictionary<string, List<int>>();
            foreach (var (idx, lbl) in manager.AllLabels)
            {
                if (!grouped.TryGetValue(lbl, out var list))
                {
                    list = new List<int>();
                    grouped[lbl] = list;
                }
                list.Add(idx);
            }

            // Sort indices for deterministic output
            foreach (var list in grouped.Values)
                list.Sort();

            var doc = new Dictionary<string, object>
            {
                ["sourceFile"] = Path.GetFileName(lasFilePath),
                ["labels"] = grouped
            };

            string json = JsonSerializer.Serialize(doc, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            string outPath = Path.ChangeExtension(lasFilePath, ".labels.json");
            File.WriteAllText(outPath, json);
            Console.WriteLine($"Labels saved: {outPath}  ({manager.Count} points)");
        }

        /// <summary>
        /// Load labels from a JSON file and apply them to the manager.
        /// </summary>
        public static bool Load(string lasFilePath, LabelManager manager)
        {
            string inPath = Path.ChangeExtension(lasFilePath, ".labels.json");
            if (!File.Exists(inPath))
            {
                Console.WriteLine($"No label file found: {inPath}");
                return false;
            }

            try
            {
                string json = File.ReadAllText(inPath);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var labels = new Dictionary<int, string>();
                if (root.TryGetProperty("labels", out var labelsObj))
                {
                    foreach (var prop in labelsObj.EnumerateObject())
                    {
                        string labelName = prop.Name;
                        foreach (var idx in prop.Value.EnumerateArray())
                        {
                            labels[idx.GetInt32()] = labelName;
                        }
                    }
                }

                manager.LoadFrom(labels);
                Console.WriteLine($"Labels loaded: {inPath}  ({labels.Count} points)");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading labels: {ex.Message}");
                return false;
            }
        }
    }
}
