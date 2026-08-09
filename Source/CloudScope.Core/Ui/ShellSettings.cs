using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudScope.Ui;

/// <summary>
/// The parts of the workspace the user expects to survive a restart: how tall they dragged
/// the command window, how wide the inspector is, and what they typed recently. Both shells
/// read and write the same file, so the tool feels the same whichever viewer was launched.
/// </summary>
public sealed class ShellSettings
{
    private const int MaxRecentInput = 100;

    /// <summary>Height of the docked command window, in text lines.</summary>
    public double HeightInLines { get; set; } = 5;

    /// <summary>Width of the properties inspector, in logical pixels.</summary>
    public double InspectorWidth { get; set; } = 280;

    /// <summary>Most recent submitted inputs, oldest first.</summary>
    public List<string> RecentInput { get; set; } = [];

    [JsonIgnore]
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "CloudScope",
        "shell.json");

    public static ShellSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new ShellSettings();

            return JsonSerializer.Deserialize<ShellSettings>(File.ReadAllText(FilePath))
                   ?? new ShellSettings();
        }
        catch (Exception)
        {
            // Corrupt or unreadable settings must never stop the application from starting.
            return new ShellSettings();
        }
    }

    public void Save()
    {
        try
        {
            if (RecentInput.Count > MaxRecentInput)
                RecentInput.RemoveRange(0, RecentInput.Count - MaxRecentInput);

            string? directory = Path.GetDirectoryName(FilePath);
            if (directory != null)
                Directory.CreateDirectory(directory);

            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SerializerOptions));
        }
        catch (Exception)
        {
            // Losing the last window height is not worth surfacing an error for.
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
}
