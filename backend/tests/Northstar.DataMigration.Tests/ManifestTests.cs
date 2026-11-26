namespace Northstar.DataMigration.Tests;

public class ManifestTests
{
    [Fact]
    public void RefreshTokensAreExplicitlyZero()
    {
        var manifest = MigrationManifest.Finalize(new MigrationManifest
        {
            Counts = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [MigrationTables.Users] = 5,
                [MigrationTables.RefreshTokens] = 0
            }
        });

        Assert.Equal(0, manifest.Counts[MigrationTables.RefreshTokens]);
        Assert.False(string.IsNullOrWhiteSpace(manifest.Hash));
    }

    [Fact]
    public void HashIsStableAcrossIdOrder()
    {
        var first = CreateManifest(new[] { "A", "B" });
        var second = CreateManifest(new[] { "B", "A" });

        Assert.Equal(first.Hash, second.Hash);
        Assert.Equal(new[] { "A", "B" }, first.UserIds);
    }

    private static MigrationManifest CreateManifest(IEnumerable<string> userIds) =>
        MigrationManifest.Finalize(new MigrationManifest
        {
            SourceDatabase = "sqlite",
            SourceRefreshTokenCount = 3,
            UserIds = userIds.ToList(),
            Counts = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [MigrationTables.Users] = 2,
                [MigrationTables.RefreshTokens] = 0
            }
        });
}
