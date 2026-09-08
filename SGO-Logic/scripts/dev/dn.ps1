param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Comando)

$raiz = (Resolve-Path "$PSScriptRoot\..\..").Path
$cadena = "Host=postgres;Port=5432;Database=sgo_ordenes;Username=ordenes_app;Password=ordenes_dev"

docker run --rm `
  -v "${raiz}:/src" `
  -v sgo_nuget:/root/.nuget/packages `
  -w /src `
  --network sgo-logic_default `
  -e ConnectionStrings__Postgres="$cadena" `
  mcr.microsoft.com/dotnet/sdk:8.0 `
  bash -lc "dotnet tool restore > /dev/null 2>&1; dotnet $($Comando -join ' ')"
