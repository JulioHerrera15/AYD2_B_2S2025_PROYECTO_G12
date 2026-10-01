# Contexto de build: raiz del repo. Construir con:
#   docker build -f src/Services/SgoLogic.Mocks.PayKlik/Dockerfile .
# Mock HTTP puro: sin base de datos, sin mensajeria, sin referencias a BuildingBlocks.

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/Services/SgoLogic.Mocks.PayKlik/SgoLogic.Mocks.PayKlik.csproj src/Services/SgoLogic.Mocks.PayKlik/
RUN dotnet restore src/Services/SgoLogic.Mocks.PayKlik/SgoLogic.Mocks.PayKlik.csproj

COPY src/Services/SgoLogic.Mocks.PayKlik/ src/Services/SgoLogic.Mocks.PayKlik/

RUN dotnet publish src/Services/SgoLogic.Mocks.PayKlik/SgoLogic.Mocks.PayKlik.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app

RUN addgroup -S sgo && adduser -S sgo -G sgo
# Directorio de la boveda de tarjetas guardadas. docker-compose monta ahi un volumen
# con nombre; al crearlo vacio hereda este dueno, asi el usuario sgo puede escribir.
RUN mkdir -p /data && chown sgo:sgo /data
USER sgo

COPY --from=build /app .

ENTRYPOINT ["dotnet", "SgoLogic.Mocks.PayKlik.dll"]
