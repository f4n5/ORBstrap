using System;
using System.IO;
using System.Text.Json;
using Crypt3d.Models;
using Crypt3d.Utilities;

namespace Crypt3d.Services
{
    public class SettingsService
    {
        private readonly string _settingsPath;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public AppSettings Prop { get; private set; } = new();

        public SettingsService()
        {
            var dir = AppInfo.DataDir;

            Directory.CreateDirectory(dir);
            _settingsPath = Path.Combine(dir, "Settings.json");
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    Prop = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                    App.Logger?.WriteLine("Settings", "Settings loaded successfully.");
                }
                else
                {
                    Prop = new AppSettings();
                    Save();
                    App.Logger?.WriteLine("Settings", "Created default settings.");
                }
            }
            catch (Exception ex)
            {
                App.Logger?.WriteException("Settings", ex);
                Prop = new AppSettings();
            }
        }

        public void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(Prop, JsonOptions);
                File.WriteAllText(_settingsPath, json);
                App.Logger?.WriteLine("Settings", "Settings saved.");
            }
            catch (Exception ex)
            {
                App.Logger?.WriteException("Settings", ex);
            }
        }
    }
}
