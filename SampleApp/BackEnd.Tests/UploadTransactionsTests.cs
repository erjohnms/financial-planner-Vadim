using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace BackEnd.Tests;

public class UploadTransactionsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public UploadTransactionsTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Upload_MultipleCsvFiles_Succeeds()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Create two sample CSV files in memory
        var csv1 = "Date,Description,Amount,Category\n2023-01-01,Test 1,-10.00,Food";
        var csv2 = "Date,Description,Amount,Category\n2023-01-02,Test 2,-20.00,Transport\n2023-01-03,Test 3,-30.00,Utilities";

        using var content = new MultipartFormDataContent();

        var fileContent1 = new StringContent(csv1);
        fileContent1.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent1, "files", "test1.csv");

        var fileContent2 = new StringContent(csv2);
        fileContent2.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        content.Add(fileContent2, "files", "test2.csv");

        // Act
        var response = await client.PostAsync("/transactions/upload?userName=TestUser", content);

        // Assert
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<UploadResult>();
        
        Assert.NotNull(result);
        // We expect 3 transactions total to be imported (1 from csv1, 2 from csv2)
        Assert.Equal(3, result.ImportedCount);
    }
    
    // We need to define UploadResult to deserialize the response properly
    private record UploadResult(int ImportedCount);
}
