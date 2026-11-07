FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/IC.Shared/IC.Shared.csproj IC.Shared/
COPY src/IC.Cozinha/IC.Cozinha.csproj IC.Cozinha/
RUN dotnet restore IC.Cozinha/IC.Cozinha.csproj

COPY src/IC.Shared/ IC.Shared/
COPY src/IC.Cozinha/ IC.Cozinha/
RUN dotnet publish IC.Cozinha/IC.Cozinha.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "IC.Cozinha.dll"]
