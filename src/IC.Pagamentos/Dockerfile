FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/IC.Shared/IC.Shared.csproj IC.Shared/
COPY src/IC.Pagamentos/IC.Pagamentos.csproj IC.Pagamentos/
RUN dotnet restore IC.Pagamentos/IC.Pagamentos.csproj

COPY src/IC.Shared/ IC.Shared/
COPY src/IC.Pagamentos/ IC.Pagamentos/
RUN dotnet publish IC.Pagamentos/IC.Pagamentos.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "IC.Pagamentos.dll"]
