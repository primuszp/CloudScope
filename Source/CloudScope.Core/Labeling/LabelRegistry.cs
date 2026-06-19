using System;
using System.Collections.Generic;
using CloudScope.Library.Enums;
using CloudScope.Loading;
using OpenTK.Mathematics;

namespace CloudScope.Labeling
{
    /// <summary>One label: a free-text name bound to an LAS classification code and a display color.</summary>
    public readonly record struct LabelDefinition(string Name, byte Code, Vector3 Color);

    /// <summary>
    /// Maps label names to ASPRS classification codes (and display colors). This is what lets the
    /// app answer "what code is this label" — used when coloring labeled points and when writing
    /// the classification field back into a LAS copy. Seeded with the standard ASPRS classes.
    /// </summary>
    public sealed class LabelRegistry
    {
        private readonly Dictionary<string, LabelDefinition> _defs = new(StringComparer.OrdinalIgnoreCase);

        public LabelRegistry(bool seedDefaults = true)
        {
            if (seedDefaults)
                SeedAsprsDefaults();
        }

        /// <summary>Raised whenever a definition is added, changed or removed.</summary>
        public event Action? Changed;

        public IReadOnlyCollection<LabelDefinition> Definitions => _defs.Values;

        public LabelDefinition Define(string name, byte code, Vector3? color = null)
        {
            string trimmed = name.Trim();
            if (trimmed.Length == 0)
                throw new ArgumentException("Label name must not be empty.", nameof(name));

            var def = new LabelDefinition(trimmed, code, color ?? ClassColorPalette.GetColor(code));
            _defs[trimmed] = def;
            Changed?.Invoke();
            return def;
        }

        public bool Remove(string name)
        {
            if (!_defs.Remove(name.Trim()))
                return false;
            Changed?.Invoke();
            return true;
        }

        public bool TryGet(string name, out LabelDefinition definition) =>
            _defs.TryGetValue(name.Trim(), out definition);

        public byte? CodeFor(string name) => TryGet(name, out LabelDefinition d) ? d.Code : null;

        /// <summary>Color for a known label; null when the name is not registered.</summary>
        public Vector3? ColorFor(string name) => TryGet(name, out LabelDefinition d) ? d.Color : null;

        public void LoadFrom(IEnumerable<LabelDefinition> definitions)
        {
            _defs.Clear();
            foreach (LabelDefinition def in definitions)
                _defs[def.Name] = def;
            Changed?.Invoke();
        }

        private void SeedAsprsDefaults()
        {
            ReadOnlySpan<ClassificationType> seeded =
            [
                ClassificationType.Unclassified,
                ClassificationType.Ground,
                ClassificationType.LowVegetation,
                ClassificationType.MediumVegetation,
                ClassificationType.HighVegetation,
                ClassificationType.Building,
                ClassificationType.Water,
                ClassificationType.Rail,
                ClassificationType.RoadSurface,
                ClassificationType.WireConductor,
                ClassificationType.TransmissionTower,
                ClassificationType.BridgeDeck
            ];

            foreach (ClassificationType type in seeded)
            {
                byte code = (byte)type;
                _defs[type.ToString()] = new LabelDefinition(type.ToString(), code, ClassColorPalette.GetColor(code));
            }
        }
    }
}
