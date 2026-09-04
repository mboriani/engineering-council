using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace FragileReliability;

// Reliability fixture: several deliberate resilience gaps.
public sealed class ShipmentClient
{
    // Static HttpClient with NO timeout configured: a hung dependency blocks forever.
    private static readonly HttpClient Http = new();

    public async Task<string> TrackAsync(string trackingNumber)
    {
        // No timeout, no cancellation token, no retry policy.
        var response = await Http.GetAsync($"https://carrier.example.com/track/{trackingNumber}");
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<string> TrackQuietlyAsync(string trackingNumber)
    {
        try
        {
            return await TrackAsync(trackingNumber);
        }
        catch (Exception)
        {
            // Swallowed exception: the failure disappears with no logging.
            return string.Empty;
        }
    }

    public void StartPolling()
    {
        // Fire-and-forget loop: unobserved task, no cancellation, no backoff.
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await TrackQuietlyAsync("latest");
                await Task.Delay(1000);
            }
        });
    }
}
