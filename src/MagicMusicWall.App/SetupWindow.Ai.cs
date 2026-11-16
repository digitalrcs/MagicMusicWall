using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using MagicMusicWall.Core;

namespace MagicMusicWall.App;

public partial class SetupWindow
{
    private void InitializeAi()
    {
        AiProvider.ItemsSource = new[] { "LM Studio", "OpenAI", "Claude", "DeepSeek", "OpenAI-compatible" };
        AiProvider.SelectedItem = _draft.AiProvider;
        AiBaseUrl.Text = _draft.AiBaseUrl;
        AiModel.Text = _draft.AiModel;
        AiStatus.Text = string.IsNullOrEmpty(_draft.AiKeyProtected) ? "No AI key saved." : "An AI key is saved for its configured provider and endpoint.";
    }
    private void AiProvider_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (AiBaseUrl is null || AiModel is null) return;
        AiBaseUrl.Text = (AiProvider.SelectedItem as string) switch
        {
            "OpenAI" => "https://api.openai.com/v1", "Claude" => "https://api.anthropic.com/v1",
            "DeepSeek" => "https://api.deepseek.com/v1", "LM Studio" => "http://localhost:1234/v1", _ => ""
        };
        AiModel.Text = "";
        AiModel.ItemsSource = null;
        AiKey?.Clear();
    }
    private void SaveAi(AppPreferences updated)
    {
        updated.AiProvider = AiProvider.SelectedItem as string ?? "LM Studio";
        updated.AiBaseUrl = AiBaseUrl.Text.Trim().TrimEnd('/');
        updated.AiModel = AiModel.Text.Trim();
        AiPlaylistService.Endpoint(new(updated.AiProvider, updated.AiBaseUrl, updated.AiModel, ""), "models");
        if (RemoveAiKey.IsChecked == true || updated.AiKeyEndpoint != updated.AiBaseUrl || updated.AiKeyProvider != updated.AiProvider)
            updated.AiKeyProtected = "";
        if (RemoveAiKey.IsChecked != true && AiKey.Password.Length > 0) updated.AiKeyProtected = AiCredentials.Protect(AiKey.Password);
        updated.AiKeyEndpoint = updated.AiBaseUrl;
        updated.AiKeyProvider = updated.AiProvider;
    }
    private async void AiModels_Click(object sender, RoutedEventArgs e) => await RunOperation(async () =>
    {
        var settings = _main.Preferences;
        SaveAi(settings);
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
        var models = await new AiPlaylistService(client).ModelsAsync(AiCredentials.Options(settings), CancellationToken.None);
        var selected = AiModel.Text;
        AiModel.ItemsSource = models;
        AiModel.Text = selected;
        AiStatus.Text = $"Connected. {models.Length} model(s) available. Choose a model and Save settings.";
    });
}
