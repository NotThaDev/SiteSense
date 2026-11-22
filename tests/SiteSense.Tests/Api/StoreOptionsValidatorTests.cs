using SiteSense.Api.Store;

namespace SiteSense.Tests.Api;

public sealed class StoreOptionsValidatorTests
{
    [Fact]
    public void Validate_EmptyRootPath_Fails()
    {
        var result = new StoreOptionsValidator().Validate(null, new StoreOptions { RootPath = "" });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Validate_RootPathIsAFile_Fails()
    {
        var file = Path.GetTempFileName();
        try
        {
            var result = new StoreOptionsValidator().Validate(null, new StoreOptions { RootPath = Path.Combine(file, "store") });

            Assert.True(result.Failed);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Validate_WritableDirectory_SucceedsAndLeavesNoProbe()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sitesense-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var result = new StoreOptionsValidator().Validate(null, new StoreOptions { RootPath = dir });

            Assert.True(result.Succeeded);
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
