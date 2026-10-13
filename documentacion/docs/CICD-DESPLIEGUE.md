# CI/CD y despliegue en AWS EC2

**Responsable:** Julio Herrera (202300850)
**Proyecto:** Sistema de Gestión de Órdenes y Logística Omnicanal (SGO-Logic) — Fase 3
**Decisión asociada:** [ADR-025](../ADR/ADR-025-cicd-github-actions-y-despliegue-ec2-con-compose.md)

Esta sección del manual técnico describe cómo se construyen, prueban y despliegan los servicios, cómo está montado el entorno en AWS y cómo operarlo.

---

## 1. Resumen

| Requisito del enunciado | Cómo se cumple |
| :--- | :--- |
| Herramienta de CI/CD: GitLab o GitHub Actions | GitHub Actions (`.github/workflows/ci.yml` y `cd.yml`) |
| Etapas build, test y deploy | Jobs `build`, `test-unitarias`, `test-integracion` (CI) y `images`, `deploy` (CD) |
| No desplegar si las pruebas fallan | `images` y `deploy` dependen del job `ci` mediante `needs`; si falla, no se publica ni se despliega |
| Docker en backend y frontend | 20 imágenes (18 servicios .NET incluida la Web, y los mocks `mock-erp` y `mock-pos`) construidas en el pipeline |
| Registry | GitHub Container Registry (`ghcr.io/<propietario>/sgo-<servicio>`) |
| Despliegue en la nube | AWS EC2 con Docker Compose |

---

## 2. Flujo de trabajo (git-flow)

```
feature/*  --PR-->  develop  --PR (release)-->  main  -->  tag vN
   |                   |                          |
   CI (build+test)     CI (build+test)            CD (CI -> imágenes -> deploy -> E2E)
```

- Un pull request hacia `develop` o `main` ejecuta el **CI**. Los pushes a `develop`, `release/**` y `hotfix/**` también.
- El merge a `main` (release de git-flow) ejecuta el **CD**. Después se crea el tag `vN` del release.
- El CD también puede lanzarse a mano: GitHub → Actions → CD → *Run workflow*, eligiendo la rama.

---

## 3. Pipeline de CI (`ci.yml`)

| Job | Qué hace |
| :--- | :--- |
| `build` | `dotnet restore` y `dotnet build` de `SgoLogic.sln` en Release. `TreatWarningsAsErrors` está activo (`Directory.Build.props`), por lo que una advertencia rompe el build |
| `test-unitarias` | Ejecuta cada proyecto de `tests/` cuyo nombre no termina en `IntegrationTests` y publica los resultados `.trx` como artefacto |
| `test-integracion` | Ejecuta los proyectos `*IntegrationTests` (Inventario, Crédito, BuildingBlocks) con Testcontainers, que usa el Docker del runner `ubuntu-latest` |

Los dos jobs de pruebas corren en paralelo y dependen de `build`. Cada uno recorre todos los proyectos aunque alguno falle, y devuelve error al final si hubo al menos una falla.

Para reproducirlo en local:

```bash
cd SGO-Logic
dotnet build SgoLogic.sln -c Release
dotnet test tests/SgoLogic.Credito.Tests -c Release
```

---

## 4. Pipeline de CD (`cd.yml`)

Se dispara con el push a `main` o manualmente. Los jobs se encadenan así:

```
ci  ->  images (matriz de 20)  ->  deploy  ->  e2e
```

1. **`ci`**: invoca `ci.yml` como workflow reutilizable. Si falla, nada de lo siguiente se ejecuta.
2. **`images`**: construye en paralelo 20 imágenes y las publica en GHCR con dos tags: los 7 primeros caracteres del commit y `latest`. Usa caché de capas de GitHub Actions por servicio. Autentica con el `GITHUB_TOKEN` del workflow.
3. **`deploy`**:
   - configura la conexión SSH a la EC2 con los secrets;
   - copia a la instancia el código del commit (`git archive` de `SGO-Logic` y `Mocks`);
   - escribe `SGO-Logic/.env` desde el secret `ENV_FILE` y `SGO-Logic/.env.deploy` con `IMAGE_PREFIX` e `IMAGE_TAG`;
   - ejecuta `deploy/aws/deploy.sh` en la instancia.
4. **`e2e`**: ejecuta las pruebas E2E 01 a 05 de Playwright contra `http://<EC2_HOST>:8092`. Su fallo no revierte el despliegue; deja el run en rojo y publica el reporte de Playwright como artefacto.

### 4.1 `deploy/aws/deploy.sh` (se ejecuta en la EC2)

1. `docker login ghcr.io` con el token efímero del workflow.
2. `docker compose ... pull` para descargar las imágenes del tag.
3. `docker compose ... up -d --no-build --remove-orphans`.
4. Espera hasta 10 minutos a que `api-gateway` y `web` estén `healthy`. Si no lo logran, imprime el estado y los últimos logs y termina con error.
5. Limpia imágenes huérfanas y cierra sesión en GHCR.

Los comandos de Compose usan los dos archivos juntos y las dos variables de entorno:

```bash
docker compose --env-file .env --env-file .env.deploy \
  -f docker-compose.yml -f docker-compose.prod.yml <comando>
```

### 4.2 `docker-compose.prod.yml`

Es un override del `docker-compose.yml` del MVP. Para cada servicio con `build` define `image: ${IMAGE_PREFIX}/sgo-<servicio>:${IMAGE_TAG}`, y a todos los servicios de larga duración les agrega `restart: unless-stopped` para que sobrevivan a un reinicio de la VM. No duplica la configuración: variables, dependencias, healthchecks y volúmenes siguen viniendo del compose base.

---

## 5. Infraestructura en AWS

| Elemento | Valor |
| :--- | :--- |
| Región | us-east-2 (Ohio) |
| Instancia | `m7i-flex.large` (2 vCPU, 8 GiB) |
| Sistema operativo | Ubuntu Server 24.04 LTS |
| Disco | gp3, 30 GiB |
| IP | Dirección IP elástica asociada a la instancia |
| Software | `docker.io`, `docker-compose-v2`, `git`; swap de 4 GB |
| Directorio de trabajo | `~/sgo-logic/` (`SGO-Logic/` y `Mocks/`) |

### 5.1 Security Group

| Puerto | Servicio | Origen |
| :--- | :--- | :--- |
| 22 | SSH | `0.0.0.0/0`, solo con llave `.pem` (necesario para el despliegue desde GitHub Actions) |
| 8092 | Web | `0.0.0.0/0` |
| 8080 | API Gateway | `0.0.0.0/0` |
| 3000 | Grafana | Solo la IP de quien presenta (regla *Mi IP*) |
| 5672, 15672, 6379, 9090, 4000, 4001, 8099 y demás puertos de servicios | Infraestructura y servicios internos | Cerrados |

El compose base publica varios puertos de infraestructura en el host; en la EC2 los protege únicamente el Security Group, por lo que no se deben abrir. Para ver Grafana sin abrir el puerto 3000 se puede usar un túnel SSH:

```bash
ssh -i llave.pem -L 3000:localhost:3000 ubuntu@<EC2_HOST>
# luego abrir http://localhost:3000
```

### 5.2 Preparación de una instancia nueva

```bash
sudo apt update && sudo apt install -y docker.io docker-compose-v2 git
sudo usermod -aG docker ubuntu
sudo fallocate -l 4G /swapfile && sudo chmod 600 /swapfile && sudo mkswap /swapfile && sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
mkdir -p ~/sgo-logic
```

Después de `usermod`, cerrar y abrir la sesión SSH para que el usuario tome el grupo `docker`.

---

## 6. Secrets de GitHub

Se configuran en el repositorio: *Settings → Secrets and variables → Actions*.

| Secret | Contenido |
| :--- | :--- |
| `EC2_HOST` | IP elástica de la instancia |
| `EC2_SSH_KEY` | Contenido completo del archivo `.pem`, incluidas las líneas `BEGIN` y `END` |
| `ENV_FILE` | Contenido completo de `SGO-Logic/.env` (credenciales de PostgreSQL, RabbitMQ, Redis, JWT, clave de cifrado de Pagos, SMTP y Grafana) |

El `GITHUB_TOKEN` lo provee GitHub automáticamente en cada ejecución y se usa para publicar y descargar imágenes. El archivo `.env` está en `.gitignore` y no se sube al repositorio; el pipeline lo escribe en la instancia en cada despliegue.

Las imágenes en GHCR son privadas por defecto. Si el despliegue falla al descargarlas con el error `denied`, se deben vincular los paquetes al repositorio o hacerlos públicos desde GitHub → Packages → Package settings.

---

## 7. Operación

Todos los comandos se ejecutan en la EC2, dentro de `~/sgo-logic/SGO-Logic`. Para abreviar:

```bash
alias dc='docker compose --env-file .env --env-file .env.deploy -f docker-compose.yml -f docker-compose.prod.yml'
```

| Tarea | Comando |
| :--- | :--- |
| Estado de los servicios | `dc ps` |
| Logs de un servicio | `dc logs --tail=100 -f ordenes-api` |
| Reiniciar un servicio | `dc restart credito-api` |
| Uso de memoria por contenedor | `docker stats --no-stream` |
| Memoria y swap de la VM | `free -h` |

### 7.1 Volver a una versión anterior

Cada despliegue deja las imágenes etiquetadas con el commit. Para volver a una versión anterior:

```bash
sed -i 's/^IMAGE_TAG=.*/IMAGE_TAG=<commit_anterior_7_caracteres>/' .env.deploy
dc up -d --no-build
```

Las imágenes anteriores siguen en disco, por lo que no hace falta descargarlas de nuevo. Si ya se eliminaron, hay que autenticarse en GHCR con un token de acceso personal con permiso `read:packages` y ejecutar `dc pull` antes del `up`.

### 7.2 Problemas frecuentes

| Síntoma | Causa probable | Qué hacer |
| :--- | :--- | :--- |
| El job `deploy` falla en "Configurar SSH" | Regla del puerto 22 ausente, `EC2_HOST` incorrecto o llave mal pegada | Revisar el Security Group y que `EC2_SSH_KEY` incluya `BEGIN` y `END` |
| `pagos-api` no arranca | Falta `PAGOS_CLAVE_CIFRADO` en `ENV_FILE`, o no son 32 bytes en base64 | Corregir el secret y relanzar el CD |
| El despliegue termina con "no está healthy" | Servicio caído o falta de memoria en la VM | Revisar `dc ps`, `dc logs` y `free -h` |
| `denied` al descargar imágenes | Paquetes privados no vinculados al repositorio | Ver sección 6 |
| El job `images` agota el tiempo en el primer despliegue | No hay caché de capas | Relanzar; las siguientes ejecuciones usan la caché |

---

## 8. Alcance y limitaciones

- La instancia es un único nodo: no hay alta disponibilidad, y las bases de datos usan volúmenes locales sin respaldo automático.
- Con 8 GiB de memoria los 26 servicios corren con poco margen. Las pruebas de carga y estrés deben ejecutarse desde fuera de la instancia.
- La prueba E2E-06 depende del endpoint interno de Crédito (`/interno/credito/demo/reiniciar`, puerto 8099), que no está expuesto en la nube, así que no corre en el pipeline y se ejecuta de forma manual en local.
- Kubernetes on-premises sigue siendo la plataforma de producción según ADR-001. [`deploy/k8s/consulta-estado-hpa.yaml`](../../deploy/k8s/consulta-estado-hpa.yaml) conserva la evidencia documental del escalado horizontal por HPA.
