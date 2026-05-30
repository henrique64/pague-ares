# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar arquivos de projeto para cache do restore
COPY Ares.PagueAres.sln                                                          ./
COPY Ares.PagueAres.API/Ares.PagueAres.API.csproj                               Ares.PagueAres.API/
COPY Ares.PagueAres.Application/Ares.PagueAres.Application.csproj               Ares.PagueAres.Application/
COPY Ares.PagueAres.Domain/Ares.PagueAres.Domain.csproj                         Ares.PagueAres.Domain/
COPY Ares.PagueAres.Infrastructure/Ares.PagueAres.Infrastructure.csproj         Ares.PagueAres.Infrastructure/

RUN dotnet restore

COPY . .

RUN dotnet publish Ares.PagueAres.API/Ares.PagueAres.API.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .
COPY entrypoint.sh ./entrypoint.sh
RUN chmod +x ./entrypoint.sh

EXPOSE 8080

ENTRYPOINT ["./entrypoint.sh"]
