using System.Text.Json;
using Autobots.Desktop;
using Xunit;

namespace Autobots.Core.Tests;

public sealed class AgentRunLogTests
{
    [Fact]
    public void DisabledLogAndUnwritableDestinationDoNotBlockTasks()
    {
        using var disabled = new AgentRunLog(null);
        disabled.Write("started", new { steps = 0 });
        var file = Path.GetTempFileName();
        try
        {
            using var unavailable = new AgentRunLog(file);
            unavailable.Write("finished", new { outcome = "Stopped" });
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public void RecordsLongRunsInOrderAndToleratesWritesAfterDisposal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "autobots-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            var log = new AgentRunLog(directory);
            for (var sequence = 1; sequence <= 100; sequence++)
                log.Write("executed", new { sequence });
            log.Write("finished", new { outcome = "Completed", steps = 100 });
            log.Dispose();
            log.Write("ignored", new { });
            log.Dispose();
            var lines = File.ReadAllLines(Assert.Single(Directory.GetFiles(directory)));
            Assert.Equal(101, lines.Length);
            for (var index = 0; index < 100; index++)
            {
                using var entry = JsonDocument.Parse(lines[index]);
                Assert.Equal(index + 1, entry.RootElement.GetProperty("details").GetProperty("sequence").GetInt32());
            }
            using var final = JsonDocument.Parse(lines[^1]);
            Assert.Equal("finished", final.RootElement.GetProperty("kind").GetString());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
