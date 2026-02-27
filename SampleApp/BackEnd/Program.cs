using Microsoft.AspNetCore.OpenApi;
using Microsoft.Data.Sqlite;
using Scalar.AspNetCore;
using System.Globalization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, ct) =>
    {
        document.Servers = [];
        document.Info.Title = "Financial Planner API";
        return Task.CompletedTask;
    });
});

builder.Services.AddAntiforgery();
builder.Services.AddSingleton<TransactionRepository>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapPost("/users/login", (LoginRequest request, TransactionRepository repository) =>
{
    var userName = request.Name?.Trim();
    if (string.IsNullOrWhiteSpace(userName))
    {
        return Results.BadRequest("Name is required.");
    }

    repository.EnsureUser(userName);
    return Results.Ok(new LoginResponse(userName));
})
.WithName("LoginUser");

app.MapPost("/transactions/upload", async (string userName, IFormFile file, TransactionRepository repository) =>
{
    if (string.IsNullOrWhiteSpace(userName))
    {
        return Results.BadRequest("userName is required.");
    }

    if (file is null || file.Length == 0)
    {
        return Results.BadRequest("CSV file is required.");
    }

    try
    {
        using var stream = file.OpenReadStream();
        var transactions = ParseTransactions(stream);
        repository.AddTransactions(userName.Trim(), file.FileName, transactions);
        return Results.Ok(new UploadResult(transactions.Count));
    }
    catch (InvalidDataException ex)
    {
        return Results.BadRequest(ex.Message);
    }
})
.WithName("UploadTransactions")
.DisableAntiforgery();

app.MapGet("/transactions", (string userName, TransactionRepository repository) =>
{
    if (string.IsNullOrWhiteSpace(userName))
    {
        return Results.BadRequest("userName is required.");
    }

    var transactions = repository.GetByUser(userName.Trim());
    return Results.Ok(transactions);
})
.WithName("GetTransactions");

app.MapDelete("/transactions", (string userName, TransactionRepository repository) =>
{
    if (string.IsNullOrWhiteSpace(userName))
    {
        return Results.BadRequest("userName is required.");
    }

    repository.DeleteByUser(userName.Trim());
    return Results.NoContent();
})
.WithName("DeleteTransactions");

app.Run();

static List<TransactionRecord> ParseTransactions(Stream csvStream)
{
    using var reader = new StreamReader(csvStream);
    string? headerLine = null;
    while (!reader.EndOfStream && string.IsNullOrWhiteSpace(headerLine))
    {
        headerLine = reader.ReadLine();
    }

    if (string.IsNullOrWhiteSpace(headerLine))
    {
        throw new InvalidDataException("CSV file is empty.");
    }

    var delimiter = DetectDelimiter(headerLine);
    var headers = ParseCsvLine(headerLine, delimiter);
    var dateIndex = FindHeaderIndex(headers, "date", "transactiondate", "posteddate");
    var descriptionIndex = FindHeaderIndex(headers, "description", "desc");
    var amountIndex = FindHeaderIndex(headers, "amount", "amt", "value");
    var categoryIndex = FindHeaderIndex(headers, "category");

    if (descriptionIndex < 0 || amountIndex < 0 || categoryIndex < 0)
    {
        throw new InvalidDataException("CSV must include description, amount, and category columns.");
    }

    var transactions = new List<TransactionRecord>();
    while (!reader.EndOfStream)
    {
        var line = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(line))
        {
            continue;
        }

        var columns = ParseCsvLine(line, delimiter);
        if (columns.Count <= Math.Max(descriptionIndex, Math.Max(amountIndex, categoryIndex)))
        {
            throw new InvalidDataException("One or more rows have missing columns.");
        }

        var dateText = dateIndex >= 0 && dateIndex < columns.Count ? columns[dateIndex].Trim() : null;
        var description = columns[descriptionIndex].Trim();
        var category = columns[categoryIndex].Trim();
        var amountText = columns[amountIndex].Trim();

        if (!decimal.TryParse(amountText, NumberStyles.Currency, CultureInfo.InvariantCulture, out var amount))
        {
            throw new InvalidDataException($"Invalid amount value '{amountText}'.");
        }

        transactions.Add(new TransactionRecord(NormalizeDate(dateText), description, amount, category));
    }

    return transactions;
}

static string? NormalizeDate(string? dateText)
{
    if (string.IsNullOrWhiteSpace(dateText))
    {
        return null;
    }

    if (DateOnly.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
        || DateOnly.TryParse(dateText, out date))
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    if (DateTime.TryParse(dateText, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime)
        || DateTime.TryParse(dateText, out dateTime))
    {
        return DateOnly.FromDateTime(dateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    return dateText;
}

static char DetectDelimiter(string headerLine)
{
    var commaCount = headerLine.Count(c => c == ',');
    var tabCount = headerLine.Count(c => c == '\t');
    return tabCount > commaCount ? '\t' : ',';
}

static int FindHeaderIndex(IReadOnlyList<string> headers, params string[] acceptedNames)
{
    for (var i = 0; i < headers.Count; i++)
    {
        var normalizedHeader = headers[i].Trim();
        if (acceptedNames.Any(name => string.Equals(name, normalizedHeader, StringComparison.OrdinalIgnoreCase)))
        {
            return i;
        }
    }

    return -1;
}

static List<string> ParseCsvLine(string line, char delimiter)
{
    var columns = new List<string>();
    var current = new System.Text.StringBuilder();
    var inQuotes = false;

    for (var i = 0; i < line.Length; i++)
    {
        var character = line[i];
        if (character == '"')
        {
            if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
            {
                current.Append('"');
                i++;
                continue;
            }

            inQuotes = !inQuotes;
            continue;
        }

        if (character == delimiter && !inQuotes)
        {
            columns.Add(current.ToString());
            current.Clear();
            continue;
        }

        current.Append(character);
    }

    columns.Add(current.ToString());
    return columns;
}

internal sealed class TransactionRepository
{
    private readonly string _connectionString;

    public TransactionRepository(IHostEnvironment environment)
    {
        var databasePath = Path.Combine(environment.ContentRootPath, "budgeting.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default
        }.ToString();

        InitializeDatabase();
    }

    public void EnsureUser(string userName)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR IGNORE INTO users(name)
            VALUES ($name);
            """;
        command.Parameters.AddWithValue("$name", userName);
        command.ExecuteNonQuery();
    }

    public void AddTransactions(string userName, string sourceFileName, IReadOnlyCollection<TransactionRecord> transactions)
    {
        if (transactions.Count == 0)
        {
            return;
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        using (var ensureUserCommand = connection.CreateCommand())
        {
            ensureUserCommand.Transaction = transaction;
            ensureUserCommand.CommandText =
                """
                INSERT OR IGNORE INTO users(name)
                VALUES ($name);
                """;
            ensureUserCommand.Parameters.AddWithValue("$name", userName);
            ensureUserCommand.ExecuteNonQuery();
        }

        var uploadedAt = DateTimeOffset.UtcNow.ToString("O");
        foreach (var record in transactions)
        {
            using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText =
                """
                INSERT INTO transactions(user_name, transaction_date, description, amount, category, source_file, uploaded_at_utc)
                VALUES ($userName, $transactionDate, $description, $amount, $category, $sourceFile, $uploadedAt);
                """;
            insertCommand.Parameters.AddWithValue("$userName", userName);
            insertCommand.Parameters.AddWithValue("$transactionDate", (object?)record.Date ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue("$description", record.Description);
            insertCommand.Parameters.AddWithValue("$amount", record.Amount);
            insertCommand.Parameters.AddWithValue("$category", record.Category);
            insertCommand.Parameters.AddWithValue("$sourceFile", sourceFileName);
            insertCommand.Parameters.AddWithValue("$uploadedAt", uploadedAt);
            insertCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public IReadOnlyList<TransactionRecord> GetByUser(string userName)
    {
        var records = new List<TransactionRecord>();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT transaction_date, description, amount, category
            FROM transactions
            WHERE user_name = $userName
            ORDER BY id DESC;
            """;
        command.Parameters.AddWithValue("$userName", userName);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            records.Add(new TransactionRecord(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.GetString(1),
                reader.GetDecimal(2),
                reader.GetString(3)));
        }

        return records;
    }

    public void DeleteByUser(string userName)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM transactions
            WHERE user_name = $userName;
            """;
        command.Parameters.AddWithValue("$userName", userName);
        command.ExecuteNonQuery();
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS users (
                name TEXT NOT NULL PRIMARY KEY
            );

            CREATE TABLE IF NOT EXISTS transactions (
                id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                user_name TEXT NOT NULL,
                transaction_date TEXT NULL,
                description TEXT NOT NULL,
                amount REAL NOT NULL,
                category TEXT NOT NULL,
                source_file TEXT NOT NULL,
                uploaded_at_utc TEXT NOT NULL,
                FOREIGN KEY(user_name) REFERENCES users(name)
            );
            """;
        command.ExecuteNonQuery();

        using var columnCheckCommand = connection.CreateCommand();
        columnCheckCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM pragma_table_info('transactions')
            WHERE name = 'transaction_date';
            """;

        var hasDateColumn = Convert.ToInt32(columnCheckCommand.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
        if (!hasDateColumn)
        {
            using var addDateColumnCommand = connection.CreateCommand();
            addDateColumnCommand.CommandText = "ALTER TABLE transactions ADD COLUMN transaction_date TEXT NULL;";
            addDateColumnCommand.ExecuteNonQuery();
        }
    }
}

internal record LoginRequest(string Name);
internal record LoginResponse(string Name);
internal record UploadResult(int ImportedCount);
internal record TransactionRecord(string? Date, string Description, decimal Amount, string Category);
