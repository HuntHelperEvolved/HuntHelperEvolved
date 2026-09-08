using HuntHelperEvolved;
using Xunit;

public class ReleaseNotesTests
{
    [Theory]
    [InlineData("0.5.0.5", "0.5.0.4", true)]
    [InlineData("0.5.0.6", "0.5.0.5", true)]
    [InlineData("0.5.0.10", "0.5.0.9", true)]
    [InlineData("0.5.0.5", "0.5.0.5", false)]
    [InlineData("0.5.0.4", "0.5.0.5", false)]
    [InlineData("0.5.0.1", "0.5.0", true)]
    [InlineData("0.5.0", "0.5.0.0", false)]
    [InlineData("0.10.0", "0.9.0.9", true)]
    [InlineData("0.5.0.6", "", true)]
    public void ComparesAllVersionComponents(string current, string previous, bool expected)
        => Assert.Equal(expected, ReleaseNotes.IsNewerThan(current, previous));
}
