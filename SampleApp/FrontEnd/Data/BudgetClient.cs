using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components.Forms;

namespace FrontEnd.Data;

public class BudgetClient
{
    private readonly HttpClient _httpClient;

    public BudgetClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> LoginAsync(string name)
    {
        var response = await _httpClient.PostAsJsonAsync("/users/login", new LoginRequest(name));
        response.EnsureSuccessStatusCode();

        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>();
        return loginResponse?.Name ?? name;
    }

    public async Task<int> UploadStatementAsync(string userName, IBrowserFile csvFile)
    {
        using var stream = csvFile.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024);
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(stream);

        fileContent.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(csvFile.ContentType) ? "text/csv" : csvFile.ContentType);

        content.Add(fileContent, "file", csvFile.Name);

        var requestUri = $"/transactions/upload?userName={Uri.EscapeDataString(userName)}";
        var response = await _httpClient.PostAsync(requestUri, content);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorText)
                ? "Upload failed."
                : errorText);
        }

        var result = await response.Content.ReadFromJsonAsync<UploadResponse>();
        return result?.ImportedCount ?? 0;
    }

    public async Task<IReadOnlyList<BudgetTransaction>> GetTransactionsAsync(string userName)
    {
        var requestUri = $"/transactions?userName={Uri.EscapeDataString(userName)}";
        var transactions = await _httpClient.GetFromJsonAsync<List<BudgetTransaction>>(requestUri);
        return transactions ?? [];
    }

    public async Task DeleteTransactionsAsync(string userName)
    {
        var requestUri = $"/transactions?userName={Uri.EscapeDataString(userName)}";
        var response = await _httpClient.DeleteAsync(requestUri);

        if (!response.IsSuccessStatusCode)
        {
            var errorText = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorText)
                ? "Delete failed."
                : errorText);
        }
    }

    private sealed record LoginRequest(string Name);
    private sealed record LoginResponse(string Name);
    private sealed record UploadResponse(int ImportedCount);
}
