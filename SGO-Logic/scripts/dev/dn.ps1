param(
  # Monta el socket de Docker y fija las variables de Testcontainers para poder
  # correr las suites de integracion dentro del contenedor del SDK.
  [switch]$Docker,
  [Parameter(ValueFromRemainingArguments = $true)][string[]]$Comando
)

$raiz = (Resolve-Path "$PSScriptRoot\..\..").Path
$cadena = "Host=db-operacional;Port=5432;Database=sgo_ordenes;Username=sgo_admin;Password=change_me"

$argumentosDocker = @(
  "-v", "${raiz}:/src",
  "-v", "sgo_nuget:/root/.nuget/packages",
  "-w", "/src",
  "--network", "sgo-logic_default",
  "-e", "ConnectionStrings__Postgres=$cadena"
)

if ($Docker) {
  # Testcontainers habla con el daemon del host por el socket montado. Los
  # contenedores que levanta publican sus puertos en el host (Docker Desktop), y
  # desde este contenedor el host se alcanza como host.docker.internal. Ryuk (el
  # limpiador de Testcontainers) necesita la ruta del socket tal como la ve el
  # daemon, que en Docker Desktop para Windows es /var/run/docker.sock.
  $argumentosDocker += @(
    "-v", "/var/run/docker.sock:/var/run/docker.sock",
    "--add-host", "host.docker.internal:host-gateway",
    "-e", "DOCKER_HOST=unix:///var/run/docker.sock",
    "-e", "TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal",
    "-e", "TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/var/run/docker.sock"
  )
}

docker run --rm @argumentosDocker `
  mcr.microsoft.com/dotnet/sdk:8.0 `
  bash -lc "dotnet tool restore > /dev/null 2>&1; dotnet $($Comando -join ' ')"
