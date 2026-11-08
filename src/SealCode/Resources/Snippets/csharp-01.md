The caller cancels its own wait while this request is pending. Does that cancel the HTTP request too?

```csharp
async Task<bool> IsSuccessfulAsync(
    HttpClient client, string url)
{
    using var response = await client
        .GetAsync(url);
    return response.IsSuccessStatusCode;
}
```
