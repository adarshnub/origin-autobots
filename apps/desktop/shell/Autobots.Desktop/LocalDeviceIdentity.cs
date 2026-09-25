namespace Autobots.Desktop;

public static class LocalDeviceIdentity
{
    public static Guid GetOrCreate()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Origin Studios", "Autobots");
        var path = Path.Combine(directory, "device-id");
        try
        {
            var value = File.ReadAllText(path).Trim();
            if (Guid.TryParse(value, out var existing) && existing != Guid.Empty)
                return existing;
        }
        catch (IOException)
        {
            // A fresh local ID is created below when none was saved yet.
        }

        Directory.CreateDirectory(directory);
        var deviceId = Guid.NewGuid();
        File.WriteAllText(path, deviceId.ToString("D"));
        return deviceId;
    }
}
