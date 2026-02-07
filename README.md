# SealCode

SealCode is a self-hosted collaborative code editor with real-time rooms. The backend is ASP.NET Core Minimal API + SignalR, and the browser UI uses Monaco Editor with Yjs for CRDT syncing. Rooms are in-memory only (no database or persistence).

![Screenshot](Room.png)

**Features**
- Real-time multi-user editing with presence and cursors.
- Room-based sessions with an admin UI to create and close rooms.
- Syntax highlighting with language switching.
- In-memory state only; everything resets when the server restarts.
- Configurable room capacity (1-5 users).

**Syntax Highlighting**
- C# (`csharp`)
- SQL (`sql`)

**Collaboration (Yjs)**
SealCode uses Yjs (CRDT) to merge concurrent edits. Each client produces incremental updates that the server broadcasts to the room.

**Quick Start**
1. Run the server:

```bash
dotnet run --project src/SealCode/SealCode.csproj
```

2. Open the app at `http://localhost:5000` (or the URL shown in the console).

**Admin Workflow**
1. Visit `/admin/login`.
2. Sign in with a user from `src/SealCode/appsettings.json` (`AdminUsers`).
3. Create a room and share its link with participants.

**Join a Room**
Open the room link in a browser and enter a display name.

**Configuration**
- `src/SealCode/appsettings.json`
- `AdminUsers`: list of admin name/password pairs.
- `MaxUsersPerRoom`: integer from 1 to 5.

**Endpoints**
- `/` landing page
- `/room/{roomId}` room UI
- `/admin` admin panel
- `/admin/login` admin login
- `/health` health check
- `/roomHub` SignalR hub