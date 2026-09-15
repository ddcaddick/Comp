# Built from the repo root (docker build -f src/Comp.Api/Dockerfile .) so the project
# reference graph -- Comp.Api depends on Application/Infrastructure/Contracts, which in
# turn depend on Domain/Scoring -- is all in the build context.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, with just the .csproj files, so an ordinary code change doesn't invalidate
# Docker's layer cache for the (slow) NuGet restore step.
COPY src/Comp.Domain/Comp.Domain.csproj src/Comp.Domain/
COPY src/Comp.Contracts/Comp.Contracts.csproj src/Comp.Contracts/
COPY src/Comp.Scoring/Comp.Scoring.csproj src/Comp.Scoring/
COPY src/Comp.Application/Comp.Application.csproj src/Comp.Application/
COPY src/Comp.Infrastructure/Comp.Infrastructure.csproj src/Comp.Infrastructure/
COPY src/Comp.Api/Comp.Api.csproj src/Comp.Api/
RUN dotnet restore src/Comp.Api/Comp.Api.csproj

COPY src/ src/
RUN dotnet publish src/Comp.Api/Comp.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Npgsql negotiates GSS encryption as part of connecting even though this app only ever
# authenticates by password -- without libgssapi, that negotiation itself fails to load
# before a connection can even attempt SSL, with a native "cannot open shared object file"
# error rather than anything Postgres-related. The slim ASP.NET runtime image doesn't
# include it by default. Found by actually running this image against a real Postgres
# locally before trusting it on a host that requires SSL, rather than only building it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# Render (and most PaaS hosts) inject the port to listen on via $PORT at container start,
# not build time -- the shell form of ENTRYPOINT is what makes that expansion happen.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} exec dotnet Comp.Api.dll"]
