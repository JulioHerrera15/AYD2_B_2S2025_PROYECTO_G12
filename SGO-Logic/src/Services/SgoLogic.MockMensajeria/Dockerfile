# Contexto de build: raiz del repo
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/BuildingBlocks/SgoLogic.Contracts/SgoLogic.Contracts.csproj src/BuildingBlocks/SgoLogic.Contracts/
COPY src/BuildingBlocks/SgoLogic.BuildingBlocks/SgoLogic.BuildingBlocks.csproj src/BuildingBlocks/SgoLogic.BuildingBlocks/
COPY src/Services/SgoLogic.MockMensajeria/SgoLogic.MockMensajeria.csproj src/Services/SgoLogic.MockMensajeria/
RUN dotnet restore src/Services/SgoLogic.MockMensajeria/SgoLogic.MockMensajeria.csproj

COPY src/BuildingBlocks/SgoLogic.Contracts/ src/BuildingBlocks/SgoLogic.Contracts/
COPY src/BuildingBlocks/SgoLogic.BuildingBlocks/ src/BuildingBlocks/SgoLogic.BuildingBlocks/
COPY src/Services/SgoLogic.MockMensajeria/ src/Services/SgoLogic.MockMensajeria/

RUN dotnet publish src/Services/SgoLogic.MockMensajeria/SgoLogic.MockMensajeria.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app

RUN addgroup -S sgo && adduser -S sgo -G sgo
USER sgo

COPY --from=build /app .

ENTRYPOINT ["dotnet", "SgoLogic.MockMensajeria.dll"]
