namespace SI360.GateRunner.Services;

public static class ReportRetentionPruner
{
    public static int Prune(string resultsDirectory, int retentionDays, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(resultsDirectory) || !Directory.Exists(resultsDirectory))
            return 0;

        var cutoff = nowUtc.UtcDateTime.AddDays(-Math.Max(1, retentionDays));
        var deleted = 0;
        foreach (var path in Directory.EnumerateFileSystemEntries(resultsDirectory, "GateRun_*"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(path) >= cutoff)
                    continue;

                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                else if (File.Exists(path))
                    File.Delete(path);

                deleted++;
            }
            catch
            {
                // Retention must never fail a gate run. Locked files remain for the next pruning pass.
            }
        }

        return deleted;
    }
}
