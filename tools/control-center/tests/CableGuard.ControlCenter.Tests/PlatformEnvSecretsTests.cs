using CableGuard.ControlCenter.Core.Services;
using Xunit;

namespace CableGuard.ControlCenter.Tests;

public sealed class PlatformEnvSecretsTests
{
    [Fact]
    public void TryReadDotEnv_reads_unquoted_and_quoted_values()
    {
        var path = Path.Combine(Path.GetTempPath(), "cg-dotenv-" + Guid.NewGuid().ToString("N") + ".env");
        try
        {
            File.WriteAllText(path, """
                # comment
                CABLEGUARD_INGEST_API_KEY=plain-key
                OTHER=1
                """);
            Assert.Equal("plain-key", PlatformEnvSecrets.TryReadDotEnv(path, PlatformEnvSecrets.IngestApiKey));

            File.WriteAllText(path, "CABLEGUARD_INGEST_API_KEY=\"quoted-key\"\n");
            Assert.Equal("quoted-key", PlatformEnvSecrets.TryReadDotEnv(path, PlatformEnvSecrets.IngestApiKey));
        }
        finally
        {
            try { File.Delete(path); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TryGet_prefers_process_env_over_dotenv()
    {
        var root = Path.Combine(Path.GetTempPath(), "cg-env-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var prev = Environment.GetEnvironmentVariable(PlatformEnvSecrets.IngestApiKey);
        try
        {
            File.WriteAllText(Path.Combine(root, ".env"), "CABLEGUARD_INGEST_API_KEY=from-file\n");
            Environment.SetEnvironmentVariable(PlatformEnvSecrets.IngestApiKey, "from-process");
            Assert.Equal("from-process", PlatformEnvSecrets.TryGet(PlatformEnvSecrets.IngestApiKey, root));
        }
        finally
        {
            Environment.SetEnvironmentVariable(PlatformEnvSecrets.IngestApiKey, prev);
            try { Directory.Delete(root, recursive: true); } catch { /* ignore */ }
        }
    }
}
