# Contexto de build: raiz del repo. Construir con:
#   docker build -f src/Services/SgoLogic.Mocks.SAT/Dockerfile .
# Mock HTTP puro: sin base de datos, sin mensajeria, sin referencias a BuildingBlocks.

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/Services/SgoLogic.Mocks.SAT/SgoLogic.Mocks.SAT.csproj src/Services/SgoLogic.Mocks.SAT/
RUN dotnet restore src/Services/SgoLogic.Mocks.SAT/SgoLogic.Mocks.SAT.csproj

COPY src/Services/SgoLogic.Mocks.SAT/ src/Services/SgoLogic.Mocks.SAT/

RUN dotnet publish src/Services/SgoLogic.Mocks.SAT/SgoLogic.Mocks.SAT.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app

RUN addgroup -S sgo && adduser -S sgo -G sgo
USER sgo

COPY --from=build /app .

ENTRYPOINT ["dotnet", "SgoLogic.Mocks.SAT.dll"]
