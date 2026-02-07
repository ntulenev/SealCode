# SealCode

Tiny self-hosted collaborative code editor with in-memory rooms. Uses ASP.NET Core .NET 8 Minimal API + SignalR and a simple `<textarea>` editor.

## What it is
- Simple rooms with up to 5 concurrent users.
- No database or persistence (everything is in-memory).
- Admin UI to create/kill rooms.

## How to run
```bash
dotnet run
```

Open the app at `http://localhost:5000` (or the URL shown in the console).

## Admin
1. Visit `/admin/login`.
2. Enter the admin name + password from `appsettings.json` (`AdminUsers`).
3. Create a room and copy its link.

## Join a room
Open the room link in a browser and enter a display name.
