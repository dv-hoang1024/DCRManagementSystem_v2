using System.Net.Http.Json;
using DCRManagementSystem.Helpers;

namespace DCRManagementSystem.Services;

public sealed class TeamsWebhookService
{
    private readonly AppSettings _settings;
    private static readonly HttpClient Client = new();

    public TeamsWebhookService(AppSettings settings)
    {
        _settings = settings;
    }

    public async Task SendAsync(string title, string message)
    {
        if (!_settings.Teams.Enabled || string.IsNullOrWhiteSpace(_settings.Teams.WebhookUrl))
        {
            return;
        }

        using var response = await Client.PostAsJsonAsync(
            _settings.Teams.WebhookUrl,
            new { text = $"**{title}**\n\n{message}" });

        response.EnsureSuccessStatusCode();
    }
}
