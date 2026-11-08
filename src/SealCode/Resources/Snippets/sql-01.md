How would you return the two most recent orders for every customer?

Assume Orders has OrderId, CustomerId, CreatedAt and Total. Explain why OrderId is included as a tie-breaker.

```sql
WITH RankedOrders AS (
    SELECT OrderId, CustomerId, CreatedAt, Total,
           ROW_NUMBER() OVER (
               PARTITION BY CustomerId
               ORDER BY CreatedAt DESC, OrderId DESC
           ) AS Position
    FROM Orders
)
SELECT OrderId, CustomerId, CreatedAt, Total
FROM RankedOrders
WHERE Position <= 2
ORDER BY CustomerId, Position;
```

Follow-up: When would DENSE_RANK produce a different result?
