using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CloudScope.Labeling
{
    /// <summary>
    /// Reads and writes label data as JSON.
    /// Version 2 keeps the old semantic labels map and adds optional instance groups:
    /// { "sourceFile": "...", "formatVersion": 2, "labels": { "Ground": [0,1] }, "instances": { "Tree": { "7": [2,3] } } }
    /// </summary>
    public static class LabelFileIO
    {
        /// <summary>
        /// Save all labels to a JSON file next to the source LAS.
        /// </summary>
        public static void Save(string lasFilePath, LabelManager manager)
        {
            // Invert: point→annotation  →  label→points[] and label→instance→points[].
            var grouped = new Dictionary<string, List<int>>();
            var instances = new Dictionary<string, Dictionary<string, List<int>>>();
            foreach (var (idx, annotation) in manager.AllAnnotations)
            {
                string labelName = annotation.LabelName;
                if (!grouped.TryGetValue(labelName, out var list))
                {
                    list = new List<int>();
                    grouped[labelName] = list;
                }
                list.Add(idx);

                if (annotation.InstanceId is not int instanceId)
                    continue;

                if (!instances.TryGetValue(labelName, out var labelInstances))
                {
                    labelInstances = new Dictionary<string, List<int>>();
                    instances[labelName] = labelInstances;
                }

                string instanceKey = instanceId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!labelInstances.TryGetValue(instanceKey, out var instanceList))
                {
                    instanceList = new List<int>();
                    labelInstances[instanceKey] = instanceList;
                }
                instanceList.Add(idx);
            }

            // Sort indices for deterministic output
            foreach (var list in grouped.Values)
                list.Sort();
            foreach (var labelInstances in instances.Values)
                foreach (var list in labelInstances.Values)
                    list.Sort();

            var doc = new Dictionary<string, object>
            {
                ["sourceFile"] = Path.GetFileName(lasFilePath),
                ["formatVersion"] = 2,
                ["labels"] = grouped,
                ["instances"] = instances
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

                var annotations = new Dictionary<int, PointAnnotation>();
                if (root.TryGetProperty("labels", out var labelsObj))
                {
                    foreach (var prop in labelsObj.EnumerateObject())
                    {
                        string labelName = prop.Name;
                        foreach (var idx in prop.Value.EnumerateArray())
                        {
                            annotations[idx.GetInt32()] = new PointAnnotation(labelName, null);
                        }
                    }
                }

                if (root.TryGetProperty("instances", out var instancesObj))
                {
                    foreach (var labelProp in instancesObj.EnumerateObject())
                    {
                        string labelName = labelProp.Name;
                        foreach (var instanceProp in labelProp.Value.EnumerateObject())
                        {
                            if (!int.TryParse(instanceProp.Name, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int instanceId))
                                continue;

                            foreach (var idx in instanceProp.Value.EnumerateArray())
                                annotations[idx.GetInt32()] = new PointAnnotation(labelName, instanceId);
                        }
                    }
                }

                manager.LoadFromAnnotations(annotations);
                Console.WriteLine($"Labels loaded: {inPath}  ({annotations.Count} points)");
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
