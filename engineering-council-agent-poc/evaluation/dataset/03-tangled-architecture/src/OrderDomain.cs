using System.Data.SqlClient;
using TangledArchitecture.Ui;

namespace TangledArchitecture.Domain;

// Layering violation fixture: the DOMAIN layer opens database connections itself and
// calls into the UI layer, and it is mutually dependent on the reporting module.
public sealed class OrderDomain
{
    private readonly OrderScreen _screen = new();

    public decimal Total(int orderId)
    {
        // Domain reaching directly into infrastructure (no repository abstraction).
        using var connection = new SqlConnection("Server=db;Database=orders;Trusted_Connection=true;");
        connection.Open();
        using var command = new SqlCommand($"SELECT SUM(Price) FROM Lines WHERE OrderId = {orderId}", connection);
        var total = (decimal)(command.ExecuteScalar() ?? 0m);

        // Domain reaching upward into the UI layer.
        _screen.Render(total);

        // Mutual dependency with the reporting module.
        return new OrderReporting().Adjust(total);
    }

    public decimal Surcharge() => 1.5m;
}

public sealed class OrderReporting
{
    // Circular: reporting depends back on the domain.
    public decimal Adjust(decimal total) => total + new OrderDomain().Surcharge();
}
