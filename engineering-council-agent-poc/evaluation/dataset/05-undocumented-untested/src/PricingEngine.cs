namespace UndocumentedUntested;

// No XML documentation anywhere, no tests in the repository, and one long
// branch-heavy method — a documentation/testing/maintainability fixture.
public sealed class PricingEngine
{
    public decimal Calculate(string tier, int quantity, bool loyalty, bool promo, string region, bool bulk, bool refund)
    {
        decimal price = 0m;

        if (tier == "basic") price = 10m;
        else if (tier == "standard") price = 25m;
        else if (tier == "premium") price = 60m;
        else if (tier == "enterprise") price = 150m;
        else price = 10m;

        if (quantity > 100) price *= 0.85m;
        else if (quantity > 50) price *= 0.9m;
        else if (quantity > 10) price *= 0.95m;

        if (loyalty && promo) price *= 0.8m;
        else if (loyalty) price *= 0.9m;
        else if (promo) price *= 0.95m;

        if (region == "eu") price *= 1.21m;
        else if (region == "us") price *= 1.07m;
        else if (region == "apac") price *= 1.1m;

        if (bulk && quantity > 500) price *= 0.75m;
        if (refund) price = -price;
        if (price < 0 && !refund) price = 0m;

        return price * quantity;
    }

    public string Describe(string tier) => tier + " pricing";
}
