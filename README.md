# MentorBooking

A full-stack Mentor–Student Doubt Session Booking Platform.

## Tech Stack

### Frontend
- Angular 19
- TypeScript
- Standalone Components
- Angular Router
- Reactive Forms
- SCSS

### Backend
- ASP.NET Core Web API (.NET 8)
- C#
- Entity Framework Core (planned)
- REST APIs
- JWT Authentication (planned)

### Database
- SQLite for local development (planned)

## Project Structure

MentorBooking/
- backend/ : ASP.NET Core Web API
- frontend/ : Angular 19 application
- README.md
- .gitignore

## Run Backend

Open PowerShell inside the backend folder.

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:5055"
dotnet run --no-launch-profile