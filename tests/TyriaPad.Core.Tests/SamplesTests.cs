namespace TyriaPad.Core.Tests;

public class SamplesTests
{
    [Fact]
    public void ReferenceInputBinds_IsCopiedToOutput()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "InputBinds-blaggletoad.xml");

        Assert.True(File.Exists(path), $"Not found: {path}");
    }
}
