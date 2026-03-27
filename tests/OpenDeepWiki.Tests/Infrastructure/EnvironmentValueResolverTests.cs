using OpenDeepWiki.Infrastructure;
using Xunit;

namespace OpenDeepWiki.Tests.Infrastructure;

public class EnvironmentValueResolverTests
{
    [Fact]
    public void Resolve_ShouldReturnFirstNonEmptyCandidate()
    {
        var value = EnvironmentValueResolver.Resolve(null, "", "   ", "token-key", "other-key");

        Assert.Equal("token-key", value);
    }

    [Fact]
    public void Resolve_ShouldReturnNull_WhenAllCandidatesAreEmpty()
    {
        var value = EnvironmentValueResolver.Resolve(null, "", "   ");

        Assert.Null(value);
    }
}
