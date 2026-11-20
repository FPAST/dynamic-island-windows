using DynamicIsland.Services;
using DynamicIsland.Views;
using Xunit;

namespace DynamicIsland.Tests;

public class WidgetPauseTests
{
    private static readonly PxRect Widget = new(100, 100, 260, 260);

    [Fact]
    public void AnyReasonPausesAll()
    {
        Assert.False(WidgetPause.All(false, false, false, false));
        Assert.True(WidgetPause.All(true, false, false, false));
        Assert.True(WidgetPause.All(false, true, false, false));
        Assert.True(WidgetPause.All(false, false, true, false));
        Assert.True(WidgetPause.All(false, false, false, true));
    }

    [Fact]
    public void AMaximisedWindowCoversIt()
    {
        Assert.True(WidgetPause.Covered(Widget, [new PxRect(0, 0, 1920, 1040)]));
    }

    [Fact]
    public void APartlyOverlappingWindowDoesNot()
    {
        Assert.False(WidgetPause.Covered(Widget, [new PxRect(0, 0, 200, 1040)]));
        Assert.False(WidgetPause.Covered(Widget, []));
    }

    [Fact]
    public void TwoWindowsSideBySideCoverItTogether()
    {
        // snapped left and right halves meeting across the widget
        Assert.True(WidgetPause.Covered(Widget, [new PxRect(0, 0, 180, 1040), new PxRect(180, 0, 1920, 1040)]));
        // a gap between them: part of the widget shows
        Assert.False(WidgetPause.Covered(Widget, [new PxRect(0, 0, 150, 1040), new PxRect(220, 0, 1920, 1040)]));
    }

    [Fact]
    public void AnEmptyWidgetIsNeverCovered() => Assert.False(WidgetPause.Covered(new PxRect(5, 5, 5, 5), [new PxRect(0, 0, 100, 100)]));
}

public class AtmosphereTests
{
    [Theory]
    [InlineData(45, true, WeatherAtmosphere.Kind.Mist)]
    [InlineData(61, true, WeatherAtmosphere.Kind.Rain)]
    [InlineData(81, false, WeatherAtmosphere.Kind.Rain)]
    [InlineData(95, true, WeatherAtmosphere.Kind.Rain)]
    [InlineData(73, true, WeatherAtmosphere.Kind.Snow)]
    [InlineData(86, false, WeatherAtmosphere.Kind.Snow)]
    [InlineData(0, true, WeatherAtmosphere.Kind.Glare)]
    [InlineData(0, false, WeatherAtmosphere.Kind.None)]   // no sun at night
    [InlineData(3, true, WeatherAtmosphere.Kind.None)]    // overcast: nothing moves
    public void KindByWeather(int code, bool day, WeatherAtmosphere.Kind kind) => Assert.Equal(kind, WeatherAtmosphere.For(code, day));
}
