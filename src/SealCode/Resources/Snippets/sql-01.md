How are orders numbered per customer? Why is OrderId included as a tie-breaker?

```sql
SELECT OrderId, CustomerId,
       ROW_NUMBER() OVER (
         PARTITION BY CustomerId
         ORDER BY CreatedAt DESC,
                  OrderId DESC
       ) AS Position
FROM Orders;
```
