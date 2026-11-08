What if we use DENSE_RANK and remove the ID tie-breaker? Compare orders with identical timestamps.

```sql
SELECT OrderId, CustomerId,
       DENSE_RANK() OVER (
         PARTITION BY CustomerId
         ORDER BY CreatedAt DESC
       ) AS Position
FROM Orders;
```

If we then keep Position <= 2, can a customer have more than two results?
