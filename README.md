# Registro di Cassa — Guida all'installazione

## Requisiti
- .NET 8 SDK
- MySQL 8.x

## Architettura
- **Backend**: ASP.NET Core 8 Web API (JSON, cookie auth)
- **Frontend**: HTML + CSS + jQuery in `wwwroot` (nessun Razor, nessun framework JS)
- **Database**: MySQL tramite `MySql.EntityFrameworkCore`

## Struttura del progetto

```
RegistroCassa/
├── Controllers/
│   ├── AccountController.cs        # POST /api/account/login|logout, GET /api/account/me
│   ├── GiornataController.cs       # GET|POST /api/giornata
│   ├── MovimentiController.cs      # POST|PUT|GET|DELETE /api/movimenti
│   ├── ReportController.cs         # POST /api/report/view|export  (admin)
│   └── AdminController.cs          # GET|POST /api/admin/users      (admin)
├── Models/                         # EF Core entity classes
├── Data/                           # AppDbContext + DbSeeder
├── DTOs/                           # Request/Response record types
├── Services/ReportService.cs       # Report logic + Excel export (ClosedXML)
├── wwwroot/
│   ├── index.html                  # Giornata (main page)
│   ├── login.html                  # Login
│   ├── report.html                 # Report (admin)
│   ├── utenti.html                 # Gestione utenti (admin)
│   ├── css/site.css
│   └── js/
│       ├── api.js                  # jQuery AJAX helper + utilities
│       └── navbar.js               # Shared navbar rendered via JS
├── Program.cs
└── appsettings.json
```

## Configurazione

### 1. Database MySQL
```sql
CREATE DATABASE registro_cassa CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
```

### 2. appsettings.json
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=registro_cassa;User=root;Password=TUA_PASSWORD;"
  },
  "AdminSeed": {
    "Email": "admin@tuodominio.com",
    "Password": "TuaPasswordSicura@123",
    "FullName": "Nome Amministratore"
  }
}
```

### 3. Migrazioni
```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add InitialCreate
dotnet ef database update
```
Le migrazioni vengono anche applicate automaticamente all'avvio.

### 4. Avvio
```bash
dotnet run
```
Aprire `https://localhost:5001` (o la porta configurata).

## Funzionalità per ruolo

| Funzione | Operatore | Amministratore |
|---|---|---|
| Vista e navigazione giornata | ✅ | ✅ |
| Inserimento movimento | ✅ | ✅ |
| Modifica movimento (con giustificazione) | ✅ | ✅ |
| Eliminazione movimento | ❌ | ✅ |
| Cassa inizio/fine giornata | ✅ | ✅ |
| Report quindicinale / mensile | ❌ | ✅ |
| Export Excel | ❌ | ✅ |
| Gestione utenti | ❌ | ✅ |

## Dipendenze NuGet
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 8.0.0
- `Microsoft.EntityFrameworkCore` 8.0.0
- `Microsoft.EntityFrameworkCore.Tools` 8.0.0
- `MySql.EntityFrameworkCore` 8.0.0
- `ClosedXML` 0.102.2
