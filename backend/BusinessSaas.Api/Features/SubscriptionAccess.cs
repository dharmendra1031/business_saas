using BusinessSaas.Api.Data;

namespace BusinessSaas.Api.Features;

public static class SubscriptionAccess
{
    public static bool CanOperate(BusinessSubscription subscription, DateTime now) =>
        ResolveStatus(subscription.Status, subscription.TrialEndsAtUtc, subscription.PaidStartsAtUtc,
            subscription.PaidEndsAtUtc, subscription.PaidPlanVersionId, now) is "Trial" or "Active";

    internal static string ResolveStatus(string? status, DateTime? trialEndsAtUtc,
        DateTime? paidStartsAtUtc, DateTime? paidEndsAtUtc, long? paidPlanVersionId, DateTime now) =>
        status switch
        {
            "Trial" => trialEndsAtUtc > now ? "Trial" : "TrialExpired",
            "Active" when paidStartsAtUtc <= now && paidEndsAtUtc > now && paidPlanVersionId.HasValue => "Active",
            "Suspended" => "Suspended",
            _ => "Inactive"
        };
}
