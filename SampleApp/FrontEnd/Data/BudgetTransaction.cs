namespace FrontEnd.Data;

public class BudgetTransaction
{
    public string? Date { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal Amount { get; set; }

    public string Category { get; set; } = string.Empty;
}
