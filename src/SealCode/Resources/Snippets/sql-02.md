Why does placing an order filter in ON preserve customers with no matching orders?

```sql
SELECT c.CustomerId, COUNT(o.OrderId) AS PaidOrders
FROM Customers AS c
LEFT JOIN Orders AS o
    ON o.CustomerId = c.CustomerId
   AND o.Status = 'Paid'
GROUP BY c.CustomerId;
```

Follow-up: What changes if the status predicate moves to WHERE, or if COUNT(*) replaces COUNT(o.OrderId)?
