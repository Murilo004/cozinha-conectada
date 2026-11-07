FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/IC.Shared/IC.Shared.csproj IC.Shared/
COPY src/IC.Pedidos/IC.Pedidos.csproj IC.Pedidos/
RUN dotnet restore IC.Pedidos/IC.Pedidos.csproj

COPY src/IC.Shared/ IC.Shared/
COPY src/IC.Pedidos/ IC.Pedidos/
RUN dotnet publish IC.Pedidos/IC.Pedidos.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "IC.Pedidos.dll"]
