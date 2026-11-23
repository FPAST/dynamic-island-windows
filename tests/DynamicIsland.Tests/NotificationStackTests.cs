using DynamicIsland.Views;
using Xunit;

namespace DynamicIsland.Tests;

public class NotificationStackTests
{
    private static void Sta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() => { try { body(); } catch (Exception e) { error = e; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw error;
    }

    private static NotificationStack Stack(int cards)
    {
        var st = new NotificationStack();
        for (int i = 0; i < cards; i++) st.Add(new("x", null, "Card " + i, "body", DateTime.Now, null));
        return st;
    }

    [Fact]
    public void ClosingCardsUnderTheCursorDoesNotShrinkTheIsland_LeavingShrinksOnce() => Sta(() =>
    {
        var st = Stack(5);
        st.TestHover(true);
        double open = st.TestState.ListHeight;
        var heights = new List<double>();
        st.Resized += () => heights.Add(st.TestState.ListHeight);
        for (int i = 0; i < 4; i++) st.TestClose(0);
        Assert.Equal(1, st.Count);
        Assert.Empty(heights);
        Assert.Equal(open, st.TestState.ListHeight);
        st.TestHover(false);
        Assert.Single(heights);
        Assert.True(heights[0] < open);
    });

    [Fact]
    public void ClearAllReportsEveryCardAndHidesOnceTheCursorLeaves() => Sta(() =>
    {
        var st = Stack(4);
        st.TestHover(true);
        var dismissed = new List<NotificationStack.Item>();
        st.Dismissed += dismissed.Add;
        int resized = 0;
        st.Resized += () => resized++;
        st.ClearAll();
        Assert.Equal(4, dismissed.Count);
        Assert.Equal(0, st.Count);
        Assert.Equal(0, resized);
        Assert.Equal(System.Windows.Visibility.Visible, st.Visibility);
        st.TestHover(false);
        Assert.Equal(1, resized);
        Assert.Equal(0, st.TestState.ListHeight);
        Assert.Equal(System.Windows.Visibility.Collapsed, st.Visibility);
    });

    [Fact]
    public void WithoutTheCursorTheStackShrinksAtOnce() => Sta(() =>
    {
        var st = Stack(3);
        double before = st.TestState.ListHeight;
        int resized = 0;
        st.Resized += () => resized++;
        st.TestClose(0);
        st.TestClose(0);
        Assert.Equal(2, resized);
        Assert.True(st.TestState.ListHeight < before);
    });
}
