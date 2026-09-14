# Getting Started — Environment & Repository Setup

**Milestone M0.** From an empty machine to a running local stack with CI.
Budget one evening. Commands are PowerShell on Windows; adjust paths if you are elsewhere.

---

## 1. Install the toolchain

Open PowerShell as Administrator and run these one at a time.

```powershell
winget install --id Git.Git -e
winget install --id Microsoft.DotNet.SDK.10 -e
winget install --id OpenJS.NodeJS.LTS -e
winget install --id Docker.DockerDesktop -e
```

Close and reopen PowerShell so the PATH updates, then enable pnpm and install the EF tools:

```powershell
corepack enable
corepack prepare pnpm@latest --activate
dotnet tool install --global dotnet-ef
```

Verify everything landed:

```powershell
git --version          # 2.x
dotnet --version       # 10.x
node --version         # 22.x
pnpm --version         # 9.x or 10.x
docker --version       # 27.x or later
dotnet ef --version
```

Docker Desktop needs WSL2 and a reboot on first install. WSL2 is the Windows Subsystem for
Linux — a lightweight Linux environment that ships with Windows and runs alongside it. Docker
containers are Linux processes, so Docker Desktop uses WSL2 as the engine that actually runs
them. You will not interact with it directly; Docker installs and configures it for you, and
if it is missing the installer prompts you to enable it.

If you would rather not run Docker at all, install PostgreSQL 16 natively instead and skip
step 4 — everything else is unchanged.

### VS Code extensions

Install these from the Extensions panel:

| Extension | Why |
|-----------|-----|
| C# Dev Kit | Solution explorer, debugging, test runner |
| EditorConfig for VS Code | Honours the shared formatting rules |
| Docker | Start and stop the database from the sidebar |
| Biome | Lint and format the TypeScript side |
| Tailwind CSS IntelliSense | Class autocomplete in the web app |
| Expo Tools | Expo config and manifest support |

---

## 2. Create the repository

On GitHub, create a **private** repository called `comp` with no README, licence or
`.gitignore` — you will generate those locally.

```powershell
cd ~\source\repos          # or wherever you keep projects
mkdir comp
cd comp
git init -b main
git remote add origin https://github.com/<you>/comp.git
```

Generate the .NET-aware ignore and formatting files, then extend them:

```powershell
dotnet new gitignore
dotnet new editorconfig
```

Append the client-side ignores to `.gitignore`:

```gitignore
# node / clients
node_modules/
dist/
.expo/
*.tsbuildinfo

# local env
.env
.env.local
appsettings.Development.local.json
```

Create the folder skeleton:

```powershell
mkdir src, tests, clients, clients\packages, tools, .github\workflows
```

---

## 3. The .NET solution

```powershell
dotnet new sln -n Comp

dotnet new classlib -o src/Comp.Domain
dotnet new classlib -o src/Comp.Scoring
dotnet new classlib -o src/Comp.Contracts
dotnet new classlib -o src/Comp.Application
dotnet new classlib -o src/Comp.Infrastructure
dotnet new web      -o src/Comp.Api
dotnet new xunit    -o tests/Comp.Scoring.Tests
dotnet new xunit    -o tests/Comp.Api.Tests
```

Add them all to the solution:

```powershell
dotnet sln add src/Comp.Domain src/Comp.Scoring src/Comp.Contracts `
               src/Comp.Application src/Comp.Infrastructure src/Comp.Api `
               tests/Comp.Scoring.Tests tests/Comp.Api.Tests
```

Delete the placeholder `Class1.cs` files from each `classlib`.

### Wire the references

The direction of these references is the architecture. Get them right now and the
boundaries enforce themselves later.

```powershell
dotnet add src/Comp.Application reference src/Comp.Domain src/Comp.Scoring src/Comp.Contracts
dotnet add src/Comp.Infrastructure reference src/Comp.Domain src/Comp.Application
dotnet add src/Comp.Api reference src/Comp.Application src/Comp.Infrastructure src/Comp.Contracts
dotnet add tests/Comp.Scoring.Tests reference src/Comp.Scoring
dotnet add tests/Comp.Api.Tests reference src/Comp.Api
```

**`Comp.Scoring` and `Comp.Domain` reference nothing.** That is deliberate. `Comp.Scoring` in
particular must never gain a reference to EF Core, ASP.NET or `Comp.Domain` — the moment it
does, the scoring rules stop being independently testable. `Comp.Application` is responsible
for mapping domain entities into the plain records `Comp.Scoring` accepts.

### Packages

```powershell
dotnet add src/Comp.Infrastructure package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Comp.Infrastructure package Microsoft.EntityFrameworkCore.Design
dotnet add src/Comp.Infrastructure package Microsoft.AspNetCore.Identity.EntityFrameworkCore

dotnet add src/Comp.Api package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add src/Comp.Api package Microsoft.AspNetCore.OpenApi
dotnet add src/Comp.Api package FluentValidation.DependencyInjectionExtensions
dotnet add src/Comp.Api package Serilog.AspNetCore
dotnet add src/Comp.Api package Sentry.AspNetCore

dotnet add tests/Comp.Scoring.Tests package FsCheck.Xunit
dotnet add tests/Comp.Api.Tests package Microsoft.AspNetCore.Mvc.Testing
dotnet add tests/Comp.Api.Tests package Testcontainers.PostgreSql
```

Confirm it all builds:

```powershell
dotnet build
```

---

## 4. Postgres locally

Create `docker-compose.yml` in the repository root:

```yaml
services:
  db:
    image: postgres:16
    container_name: comp-db
    environment:
      POSTGRES_USER: comp
      POSTGRES_PASSWORD: devpassword
      POSTGRES_DB: comp_dev
    ports:
      - "5432:5432"
    volumes:
      - comp-db-data:/var/lib/postgresql/data
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U comp -d comp_dev"]
      interval: 5s
      timeout: 3s
      retries: 10

volumes:
  comp-db-data:
```

Start it and check it is healthy:

```powershell
docker compose up -d
docker compose ps
```

Store the connection string in user secrets rather than `appsettings.json`, so it never
reaches the repository:

```powershell
dotnet user-secrets init --project src/Comp.Api
dotnet user-secrets set "ConnectionStrings:Default" `
  "Host=localhost;Port=5432;Database=comp_dev;Username=comp;Password=devpassword" `
  --project src/Comp.Api
```

The `devpassword` above is fine because the database only ever listens on localhost. Staging
and production connection strings live in the host's secret store and nowhere else.

---

## 5. The clients workspace

```powershell
cd clients
pnpm init
```

Create `clients/pnpm-workspace.yaml`:

```yaml
packages:
  - 'packages/*'
  - 'web'
  - 'mobile'
```

Create `clients/.npmrc`:

```
node-linker=hoisted
```

**That `.npmrc` line matters.** React Native's Metro bundler cannot follow pnpm's symlinked
`node_modules`. Without hoisting, the Expo app fails to resolve modules in ways that are
tedious to diagnose. Set it before installing anything.

### The shared core package

```powershell
mkdir packages\core\src
cd packages\core
pnpm init
pnpm add -D typescript vitest
```

Set `clients/packages/core/package.json` to:

```json
{
  "name": "@comp/core",
  "version": "0.0.0",
  "private": true,
  "type": "module",
  "main": "./src/index.ts",
  "types": "./src/index.ts",
  "scripts": {
    "test": "vitest run",
    "test:watch": "vitest",
    "typecheck": "tsc --noEmit"
  }
}
```

Drop `time.ts` and `time.test.ts` into `packages/core/src/`, and create
`packages/core/src/index.ts`:

```ts
export * from './time';
```

Add a `tsconfig.json` in the same folder:

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "module": "ESNext",
    "moduleResolution": "Bundler",
    "strict": true,
    "noUncheckedIndexedAccess": true,
    "skipLibCheck": true,
    "noEmit": true
  },
  "include": ["src"]
}
```

Then prove it works — this is the first green test in the project:

```powershell
pnpm test
```

### The web app

```powershell
cd ..\..
pnpm create vite web --template react-ts
cd web
pnpm add @comp/core@workspace:* @tanstack/react-query @tanstack/react-table react-router
pnpm add -D tailwindcss @tailwindcss/vite vitest @testing-library/react
```

### The mobile app

```powershell
cd ..
pnpm create expo-app mobile --template blank-typescript
cd mobile
pnpm add @comp/core@workspace:* @tanstack/react-query expo-router expo-secure-store
```

Then install everything from the workspace root:

```powershell
cd ..
pnpm install
```

---

## 6. Prove the stack end to end

Before writing any real feature, get one value travelling from the database to both clients.
This is a boring afternoon that saves a frustrating week.

In `src/Comp.Api/Program.cs`, add a health endpoint and permissive development CORS:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy => policy
        .WithOrigins("http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()));

var app = builder.Build();

app.UseCors();
app.MapGet("/health", () => Results.Ok(new { status = "ok", at = DateTimeOffset.UtcNow }));

app.Run();
```

Then:

1. `dotnet run --project src/Comp.Api` and open `/health` in a browser.
2. Fetch `/health` from the web app and render the timestamp.
3. Fetch `/health` from the Expo app on your phone, using your machine's LAN IP rather
   than `localhost`.

Step 3 is the one that catches problems. Your phone is a different device on the network, so
the API must listen on more than the loopback adapter and Windows Firewall must allow it.
Finding that out now is much better than finding it out in week 10 with a squad waiting.

Commit at this point:

```powershell
git add .
git commit -m "M0: solution, clients workspace, local database, health endpoint"
git push -u origin main
```

---

## 7. Continuous integration

Create `.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
    branches: [main]
  pull_request:

jobs:
  dotnet:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.x'
      - run: dotnet restore
      - run: dotnet build --no-restore --configuration Release
      - run: dotnet test --no-build --configuration Release

  clients:
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: clients
    steps:
      - uses: actions/checkout@v4
      - uses: pnpm/action-setup@v4
      - uses: actions/setup-node@v4
        with:
          node-version: '22'
          cache: pnpm
          cache-dependency-path: clients/pnpm-lock.yaml
      - run: pnpm install --frozen-lockfile
      - run: pnpm -r typecheck
      - run: pnpm -r test
```

The API type generation and its drift check join this workflow in M2, once there are real
endpoints to generate from.

---

## 8. Verification checklist

Before you call M0 done:

- [ ] `dotnet build` succeeds from the repository root
- [ ] `dotnet test` runs and passes
- [ ] `docker compose ps` shows the database healthy
- [ ] `pnpm -r test` passes, including the time module tests
- [ ] The web app loads at `localhost:5173` and shows the API timestamp
- [ ] The Expo app loads on your phone and shows the same timestamp
- [ ] CI is green on GitHub
- [ ] No connection string or secret appears anywhere in `git log`

---

## Where the daily loop settles

Four terminals, or four VS Code tasks:

```powershell
docker compose up -d                     # once, stays running
dotnet watch --project src/Comp.Api      # API, hot reload
pnpm --filter web dev                    # web, hot reload
pnpm --filter mobile start               # Expo, scan the QR code
```

## What comes next

**M2 — Data and identity.** The domain entities from section D of the architecture brief, the
EF Core schema and first migration, ASP.NET Core Identity with the four roles, and the audit
interceptor.

Two things are worth doing in that order specifically: get the audit interceptor working
*before* any real write endpoints exist, so no service is ever written without it, and get
the OpenAPI type generation and drift check into CI as soon as the first real DTO exists.

One deliberate omission: do not start building admin screens yet. The temptation after a
successful M0 is to build a shooter list because it is easy and visible. The schema and the
audit trail underneath it are what everything else depends on.
