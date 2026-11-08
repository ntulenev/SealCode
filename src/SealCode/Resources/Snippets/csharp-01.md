What does this method return, and how does cancellation propagate?

```csharp
public static async Task<int> CountSuccessfulAsync(
    HttpClient client,
    IEnumerable<string> urls,
    CancellationToken cancellationToken)
{
    var count = 0;
    foreach (var url in urls)
    {
        using var response = await client.GetAsync(url, cancellationToken);
        if (response.IsSuccessStatusCode)
            count++;
    }
    return count;
}
```

Follow-up: How would you add bounded concurrency while preserving cancellation and disposing every response?
