using RailSim.Core.Time;
using Xunit;

namespace RailSim.Core.Tests.Time;

public sealed class SimulationClockTests
{
    [Theory]
    [InlineData(SimulationSpeedPreset.Paused, 0)]
    [InlineData(SimulationSpeedPreset.Quarter, 1)]
    [InlineData(SimulationSpeedPreset.Half, 2)]
    [InlineData(SimulationSpeedPreset.Normal, 4)]
    [InlineData(SimulationSpeedPreset.X2, 8)]
    [InlineData(SimulationSpeedPreset.X4, 16)]
    [InlineData(SimulationSpeedPreset.X8, 32)]
    [InlineData(SimulationSpeedPreset.X16, 64)]
    [InlineData(SimulationSpeedPreset.X32, 128)]
    [InlineData(SimulationSpeedPreset.X64, 256)]
    public void OneRealSecondProducesExpectedSteps(
        SimulationSpeedPreset preset,
        int expectedSteps)
    {
        var clock = new SimulationClock();

        var steps = clock.Accumulate(
            TimeSpan.FromSeconds(1),
            SimulationRate.FromPreset(preset));

        Assert.Equal(expectedSteps, steps);
    }

    [Fact]
    public void TenSmallFramesEqualOneLargeFrame()
    {
        var splitClock = new SimulationClock();
        var wholeClock = new SimulationClock();
        var rate = SimulationRate.FromPreset(SimulationSpeedPreset.Normal);

        var splitSteps = 0;
        for (var index = 0; index < 10; index++)
        {
            splitSteps += splitClock.Accumulate(TimeSpan.FromMilliseconds(100), rate);
        }

        var wholeSteps = wholeClock.Accumulate(TimeSpan.FromSeconds(1), rate);

        Assert.Equal(wholeSteps, splitSteps);
        Assert.Equal(4, splitSteps);
    }

    [Fact]
    public void ChangingRatePreservesSubStepRemainder()
    {
        var clock = new SimulationClock();

        var beforeChange = clock.Accumulate(
            TimeSpan.FromMilliseconds(500),
            SimulationRate.FromPreset(SimulationSpeedPreset.Quarter));
        var afterChange = clock.Accumulate(
            TimeSpan.FromMilliseconds(125),
            SimulationRate.FromPreset(SimulationSpeedPreset.Normal));

        Assert.Equal(0, beforeChange);
        Assert.Equal(1, afterChange);
    }

    [Fact]
    public void AdvanceOneIncrementsTickExactlyOnce()
    {
        var clock = new SimulationClock();

        var first = clock.AdvanceOne();
        var second = clock.AdvanceOne();

        Assert.Equal(new SimulationTick(1), first);
        Assert.Equal(new SimulationTick(2), second);
        Assert.Equal(second, clock.Current);
    }

    [Fact]
    public void NegativeElapsedTimeIsRejected()
    {
        var clock = new SimulationClock();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            clock.Accumulate(
                TimeSpan.FromMilliseconds(-1),
                SimulationRate.FromPreset(SimulationSpeedPreset.Normal)));

        Assert.Equal("realElapsed", exception.ParamName);
    }
}
