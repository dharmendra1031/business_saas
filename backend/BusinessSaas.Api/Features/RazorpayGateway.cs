using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace BusinessSaas.Api.Features;

public sealed record GatewayOrder(string Id, long Amount, string Currency, string Status);
public sealed record GatewayPayment(string Id, string OrderId, long Amount,
    string Currency, string Status);

public interface IRazorpayGateway
{
    Task<GatewayOrder> CreateOrder(long requestId, long amount, CancellationToken cancellationToken);
    Task<GatewayPayment> FetchPayment(string paymentId, CancellationToken cancellationToken);
}

public static class RazorpayConfiguration
{
    public static bool IsEnabled(IConfiguration configuration)
    {
        if (!configuration.GetValue<bool>("Payments:Enabled") ||
            configuration["Razorpay:KeySecret"] is not { Length: >= 16 } ||
            configuration["Razorpay:WebhookSecret"] is not { Length: >= 20 })
            return false;

        var keyId = configuration["Razorpay:KeyId"] ?? "";
        var mode = configuration["Razorpay:Mode"];
        return string.Equals(mode, "Test", StringComparison.OrdinalIgnoreCase) &&
                keyId.StartsWith("rzp_test_", StringComparison.Ordinal) ||
            string.Equals(mode, "Live", StringComparison.OrdinalIgnoreCase) &&
                configuration.GetValue<bool>("Payments:AllowLive") &&
                keyId.StartsWith("rzp_live_", StringComparison.Ordinal);
    }

    public static long ToPaise(decimal amount)
    {
        if (amount <= 0 || amount > 10000000 || decimal.Truncate(amount * 100) != amount * 100)
            throw new ArgumentOutOfRangeException(nameof(amount), "INR amount must have at most two decimals.");
        return checked((long)(amount * 100));
    }
}

public sealed class RazorpayGateway(HttpClient client, IConfiguration configuration)
    : IRazorpayGateway
{
    public async Task<GatewayOrder> CreateOrder(long requestId, long amount,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, "orders");
        request.Content = JsonContent.Create(new
        {
            amount,
            currency = "INR",
            receipt = $"bs_{requestId}"
        });
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var data = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = data.RootElement;
        return new GatewayOrder(root.GetProperty("id").GetString()!,
            root.GetProperty("amount").GetInt64(),
            root.GetProperty("currency").GetString()!,
            root.GetProperty("status").GetString()!);
    }

    public async Task<GatewayPayment> FetchPayment(string paymentId,
        CancellationToken cancellationToken)
    {
        if (!paymentId.StartsWith("pay_", StringComparison.Ordinal) ||
            paymentId.Length is < 8 or > 80 ||
            !paymentId.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_'))
            throw new ArgumentException("Invalid provider payment reference.", nameof(paymentId));

        using var request = CreateRequest(HttpMethod.Get, $"payments/{paymentId}");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var data = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = data.RootElement;
        return new GatewayPayment(root.GetProperty("id").GetString()!,
            root.GetProperty("order_id").GetString()!,
            root.GetProperty("amount").GetInt64(),
            root.GetProperty("currency").GetString()!,
            root.GetProperty("status").GetString()!);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var keyId = configuration["Razorpay:KeyId"]!;
        var keySecret = configuration["Razorpay:KeySecret"]!;
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{keyId}:{keySecret}")));
        return request;
    }
}
