using System.Data.SqlClient;

namespace VulnerablePayments;

// Intentionally vulnerable fixture — do not copy.
public sealed class AccountRepository
{
    // Hardcoded credential (CWE-798).
    private const string ConnectionString = "Server=db;User Id=sa;Password=P@ssw0rd-demo-fixture;";

    // SQL built from untrusted input (CWE-89).
    public string FindByName(string name)
    {
        using var connection = new SqlConnection(ConnectionString);
        connection.Open();

        var sql = "SELECT TOP 1 Id FROM Accounts WHERE Name = '" + name + "'";
        using var command = new SqlCommand(sql, connection);
        return command.ExecuteScalar()?.ToString() ?? string.Empty;
    }
}
