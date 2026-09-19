using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Tests;

public sealed class DiagnosticServiceTests
{
    [Fact]
    public void Event_and_Flush_writes_log_to_disk()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HRO_DiagTest_{Guid.NewGuid():N}");
        string logPath = Path.Combine(tempDir, "diagnostic.log");

        try
        {
            using (var diagnostic = new DiagnosticService(logPath))
            {
                diagnostic.Event("Hello Diagnostic Log Test!");
                diagnostic.Flush();

                Assert.True(File.Exists(logPath));
                string content = diagnostic.ReadFileText();
                Assert.Contains("Hello Diagnostic Log Test!", content);
                Assert.Contains("[INFO]", content);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void GetBufferedText_returns_immediately()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HRO_DiagTest_{Guid.NewGuid():N}");
        string logPath = Path.Combine(tempDir, "diagnostic.log");

        try
        {
            using var diagnostic = new DiagnosticService(logPath);
            diagnostic.Warn("Warning in-memory immediate check");

            string buffered = diagnostic.GetBufferedText();
            Assert.Contains("Warning in-memory immediate check", buffered);
            Assert.Contains("[WARN]", buffered);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Clear_resets_both_buffer_and_file()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HRO_DiagTest_{Guid.NewGuid():N}");
        string logPath = Path.Combine(tempDir, "diagnostic.log");

        try
        {
            using var diagnostic = new DiagnosticService(logPath);
            diagnostic.Event("Message before clear");
            diagnostic.Flush();

            diagnostic.Clear();

            Assert.Empty(diagnostic.GetBufferedText());
            Assert.Empty(diagnostic.ReadFileText().Trim());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Dispose_drains_all_queued_logs()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"HRO_DiagTest_{Guid.NewGuid():N}");
        string logPath = Path.Combine(tempDir, "diagnostic.log");

        try
        {
            var diagnostic = new DiagnosticService(logPath);
            for (int i = 0; i < 30; i++)
            {
                diagnostic.Event($"Queued item #{i}");
            }

            // Dispose 必须等待后台 Worker 排空所有日志
            diagnostic.Dispose();

            Assert.True(File.Exists(logPath));
            string content = File.ReadAllText(logPath);
            for (int i = 0; i < 30; i++)
            {
                Assert.Contains($"Queued item #{i}", content);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
