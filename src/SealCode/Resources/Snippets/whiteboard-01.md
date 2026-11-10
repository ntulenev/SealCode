Client, API and database: a starting point for discussing request flow, persistence and failure handling.

```whiteboard
{
  "format": "sealcode-whiteboard",
  "version": 1,
  "shapes": [
    {
      "id": "client",
      "type": "rectangle",
      "x": 40,
      "y": 100,
      "width": 140,
      "height": 80,
      "text": "Client"
    },
    {
      "id": "request",
      "type": "arrow",
      "x": 190,
      "y": 140,
      "width": 80,
      "height": 0,
      "text": ""
    },
    {
      "id": "api",
      "type": "rectangle",
      "x": 280,
      "y": 100,
      "width": 140,
      "height": 80,
      "text": "API"
    },
    {
      "id": "query",
      "type": "bidirectional-arrow",
      "x": 430,
      "y": 140,
      "width": 80,
      "height": 0,
      "text": ""
    },
    {
      "id": "db",
      "type": "database",
      "x": 520,
      "y": 90,
      "width": 140,
      "height": 100,
      "text": "Database"
    }
  ]
}
```
