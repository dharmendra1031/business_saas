using BusinessSaas.Api.Data;

namespace BusinessSaas.Api.Features;

public static class SubscriptionAccess
{
    public static bool CanOperate(BusinessSubscription subscription, DateTime now) =>
        subscription.Status switch
        {
            "Trial" => subscription.TrialEndsAtUtc > now,
            "Active" => subscription.PaidStartsAtUtc <= now &&
                subscription.PaidEndsAtUtc > now && subscription.PaidPlanVersionId.HasValue,
            _ => false
        };
}
