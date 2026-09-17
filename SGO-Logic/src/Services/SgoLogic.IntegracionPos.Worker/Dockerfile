FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY src/BuildingBlocks/SgoLogic.Contracts/SgoLogic.Contracts.csproj src/BuildingBlocks/SgoLogic.Contracts/
COPY src/BuildingBlocks/SgoLogic.BuildingBlocks/SgoLogic.BuildingBlocks.csproj src/BuildingBlocks/SgoLogic.BuildingBlocks/
COPY src/Services/SgoLogic.IntegracionPos.Worker/SgoLogic.IntegracionPos.Worker.csproj src/Services/SgoLogic.IntegracionPos.Worker/
RUN dotnet restore src/Services/SgoLogic.IntegracionPos.Worker/SgoLogic.IntegracionPos.Worker.csproj

COPY src/BuildingBlocks/SgoLogic.Contracts/ src/BuildingBlocks/SgoLogic.Contracts/
COPY src/BuildingBlocks/SgoLogic.BuildingBlocks/ src/BuildingBlocks/SgoLogic.BuildingBlocks/
COPY src/Services/SgoLogic.IntegracionPos.Worker/ src/Services/SgoLogic.IntegracionPos.Worker/

RUN dotnet publish src/Services/SgoLogic.IntegracionPos.Worker/SgoLogic.IntegracionPos.Worker.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app

RUN addgroup -S sgo && adduser -S sgo -G sgo
USER sgo

COPY --from=build /app .

ENTRYPOINT ["dotnet", "SgoLogic.IntegracionPos.Worker.dll"]
