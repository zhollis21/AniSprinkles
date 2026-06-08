namespace AniSprinkles.UnitTests;

public class StudioTests
{
    [Fact]
    public void RoleLabel_MainStudio_IsMainStudio()
    {
        Assert.Equal("Main Studio", new Studio { IsMain = true }.RoleLabel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void RoleLabel_NonMainOrUnknown_IsStudio(bool? isMain)
    {
        Assert.Equal("Studio", new Studio { IsMain = isMain }.RoleLabel);
    }

    [Fact]
    public void DisplayName_BlankName_FallsBackToStudio()
    {
        Assert.Equal("Studio", new Studio { Name = "  " }.DisplayName);
        Assert.Equal("Toei Animation", new Studio { Name = "Toei Animation" }.DisplayName);
    }
}
