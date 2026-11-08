How does this conditional update prevent a negative stock quantity under concurrent requests?

```sql
BEGIN TRANSACTION;

UPDATE Inventory
SET Quantity = Quantity - 1
WHERE ProductId = 42
  AND Quantity > 0;

-- The application must check the affected row count.
-- Commit only if one row was updated; otherwise roll back.
COMMIT;
```

Follow-up: How would you include an order insert atomically, handle deadlocks, and prevent duplicate purchases on retry?
