
namespace MentorBooking.Api.Services;

/// <summary>
/// Defines when a Pending booking stops being eligible
/// for mentor approval and stops holding calendar time.
/// All DateTime values must represent UTC instants.
/// </summary>
public static class PendingBookingDeadline
{
    public const int ApprovalHours = 48;

    public static bool IsOverdue(
        DateTime createdAtUtc,
        DateTime scheduledStartUtc,
        DateTime nowUtc)
    {
        // Deadline 1: 48 hours after booking creation.
        var approvalCutoff = nowUtc.AddHours(-ApprovalHours);

        // Deadline 2: the scheduled session start.
        // Whichever happens first ends the Pending hold.
        return createdAtUtc <= approvalCutoff ||
               scheduledStartUtc <= nowUtc;
    }

    public static DateTime GetDeadlineUtc(
        DateTime createdAtUtc,
        DateTime scheduledStartUtc)
    {
        var deadline48Hours =
            createdAtUtc.AddHours(ApprovalHours);

        return deadline48Hours <= scheduledStartUtc
            ? deadline48Hours
            : scheduledStartUtc;
    }
}
