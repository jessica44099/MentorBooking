
using MentorBooking.Api.Services;
using Xunit;

namespace MentorBooking.Tests;

public sealed class PendingBookingDeadlineTests
{
    private static readonly DateTime TestNow =
        new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(-47, 24, false)]
    [InlineData(-48, 24, true)]
    [InlineData(-49, 24, true)]
    [InlineData(-1, 0, true)]
    [InlineData(-1, -1, true)]
    [InlineData(-1, 1, false)]
    public void IsOverdue_uses_earliest_deadline(
        int createdOffsetHours,
        int startOffsetHours,
        bool expected)
    {
        var created = TestNow.AddHours(
            createdOffsetHours);

        var scheduledStart = TestNow.AddHours(
            startOffsetHours);

        var result = PendingBookingDeadline.IsOverdue(
            created,
            scheduledStart,
            TestNow);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Deadline_is_48_hours_when_session_is_later()
    {
        var created = TestNow;
        var scheduledStart = TestNow.AddHours(72);

        var result = PendingBookingDeadline.GetDeadlineUtc(
            created,
            scheduledStart);

        Assert.Equal(TestNow.AddHours(48), result);
    }

    [Fact]
    public void Deadline_is_session_start_when_it_is_earlier()
    {
        var created = TestNow;
        var scheduledStart = TestNow.AddHours(12);

        var result = PendingBookingDeadline.GetDeadlineUtc(
            created,
            scheduledStart);

        Assert.Equal(scheduledStart, result);
    }
}
