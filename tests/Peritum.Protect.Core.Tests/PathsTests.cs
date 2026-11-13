namespace Peritum.Protect.Core.Tests;

public class PathsTests
{
    [Fact]
    public void Data_folder_stays_under_local_app_data_unless_the_environment_overrides_it()
    {
        var previous = Environment.GetEnvironmentVariable(Paths.DataEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(Paths.DataEnvironmentVariable, null);
            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Peritum",
                "Protect");
            Assert.Equal(expected, Paths.Root);
            Assert.Equal(Path.Combine(expected, "settings.json"), Paths.SettingsFile);

            Environment.SetEnvironmentVariable(Paths.DataEnvironmentVariable, "   ");
            Assert.Equal(expected, Paths.Root);

            var custom = Path.Combine(Path.GetTempPath(), "peritum-data-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable(Paths.DataEnvironmentVariable, "  " + custom + "  ");
            Assert.Equal(custom, Paths.Root);
            Assert.Equal(Path.Combine(custom, "events.jsonl"), Paths.EventsFile);
            Assert.Equal(Path.Combine(custom, "maps"), Paths.MapsDir);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Paths.DataEnvironmentVariable, previous);
        }
    }
}
