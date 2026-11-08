Why can an immutable record be useful as a dictionary key?

```csharp
public sealed record ProductKey(string Sku, string Region);

var stock = new Dictionary<ProductKey, int>
{
    [new ProductKey("BOOK-01", "EU")] = 12
};
Console.WriteLine(stock[new ProductKey("BOOK-01", "EU")]);
```

Follow-up: What can go wrong if fields participating in equality and hashing change after a key is inserted?
