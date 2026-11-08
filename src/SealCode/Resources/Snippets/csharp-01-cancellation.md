What if we accept and forward a token? When it is canceled, does this return false or throw?

```csharp
async Task<bool> IsSuccessfulAsync(
    HttpClient client, string url,
    CancellationToken token)
{
    using var response = await client
        .GetAsync(url, token);
    return response.IsSuccessStatusCode;
}
```

How would you extend this to several URLs with bounded concurrency?
