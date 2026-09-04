using System;

namespace TangledArchitecture.Ui;

// UI layer — referenced directly by the domain layer (inverted dependency).
public sealed class OrderScreen
{
    public void Render(decimal total) => Console.WriteLine($"Order total: {total:C}");
}
