using SiteSense.Api.Auth;
using SiteSense.Api.Realtime;

namespace SiteSense.Tests.Api.Realtime;

public sealed class EventAudienceTests
{
    private static readonly string[] Both = [SiteSensePolicies.CanSeeIndividuals, SiteSensePolicies.CanSeeAggregates];

    [Theory]
    [InlineData(EventTypes.PositionUpdated, SiteSensePolicies.CanSeeIndividuals)]
    [InlineData(EventTypes.AlertRaised, SiteSensePolicies.CanSeeIndividuals)]
    [InlineData(EventTypes.OccupancyChanged, SiteSensePolicies.CanSeeAggregates)]
    public void PolicyFor_EachType_ReturnsItsPolicy(string type, string policy)
    {
        Assert.Equal(policy, EventAudience.PolicyFor(type));
    }

    [Fact]
    public void CanSee_BothPolicies_SeesAllThreeTypes()
    {
        Assert.True(EventAudience.CanSee(EventTypes.PositionUpdated, Both));
        Assert.True(EventAudience.CanSee(EventTypes.AlertRaised, Both));
        Assert.True(EventAudience.CanSee(EventTypes.OccupancyChanged, Both));
    }

    [Fact]
    public void CanSee_AggregatesOnly_SeesOccupancyOnly()
    {
        var passed = new[] { SiteSensePolicies.CanSeeAggregates };

        Assert.False(EventAudience.CanSee(EventTypes.PositionUpdated, passed));
        Assert.False(EventAudience.CanSee(EventTypes.AlertRaised, passed));
        Assert.True(EventAudience.CanSee(EventTypes.OccupancyChanged, passed));
    }

    [Theory]
    [InlineData(EventTypes.PositionUpdated)]
    [InlineData(EventTypes.AlertRaised)]
    [InlineData(EventTypes.OccupancyChanged)]
    public void CanSee_NoPolicies_NobodySees(string type)
    {
        Assert.False(EventAudience.CanSee(type, []));
    }

    [Fact]
    public void CanSee_UnknownType_NobodySees()
    {
        Assert.Null(EventAudience.PolicyFor("Unknown"));
        Assert.False(EventAudience.CanSee("Unknown", Both));
    }
}
