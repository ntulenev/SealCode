What values are printed, and when does the query run?

```csharp
var numbers = new List<int> { 1, 2, 3 };
var query = numbers.Where(number => number % 2 == 0);
numbers.Add(4);
Console.WriteLine(string.Join(", ", query));
var snapshot = query.ToList();
numbers.Add(6);
Console.WriteLine(string.Join(", ", snapshot));
```

Follow-up: How would the behavior differ for an IQueryable backed by a database?
