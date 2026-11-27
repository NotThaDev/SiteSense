using System;
using SiteSense.Simulator;

namespace SiteSense.Tests.Simulator;

public sealed class SimulatorOptionsTests
{
    private static string? NoEnv(string _) => null;

    [Fact]
    public void Parse_NoArgs_Defaults()
    {
        var options = SimulatorOptions.Parse([], NoEnv);

        Assert.Equal("http://localhost:8080", options.Api);
        Assert.Equal(10, options.Devices);
        Assert.Equal(42, options.Seed);
        Assert.Null(options.StartMs);
        Assert.True(options.Setup);
    }

    [Fact]
    public void Parse_EnvAndArg_ArgWins()
    {
        string? Env(string name) => name == "SIM_API" ? "x" : null;

        var options = SimulatorOptions.Parse(["--api", "y"], Env);

        Assert.Equal("y", options.Api);
    }

    [Fact]
    public void Parse_UnknownArg_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => SimulatorOptions.Parse(["--bogus"], NoEnv));

        Assert.Contains("--bogus", ex.Message);
    }

    [Fact]
    public void Parse_NoSetup_SetupFalse()
    {
        var options = SimulatorOptions.Parse(["--no-setup"], NoEnv);

        Assert.False(options.Setup);
    }

    [Fact]
    public void Parse_BatchShorterThanInterval_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            SimulatorOptions.Parse(["--interval-s", "2", "--batch-s", "1"], NoEnv));

        Assert.Contains("--batch-s", ex.Message);
    }
}
