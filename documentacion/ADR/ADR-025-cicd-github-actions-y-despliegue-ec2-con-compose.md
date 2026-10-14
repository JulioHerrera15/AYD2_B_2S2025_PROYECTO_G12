# ADR-025: CI/CD con GitHub Actions y despliegue en AWS EC2 con Docker Compose

**Estado:** Aceptada
**Responsable:** Julio Herrera (202300850)
**Fecha:** 2026-10-12

## Contexto

El enunciado de la Fase 3 exige un proceso de integración y despliegue continuo con tres etapas (build, test y deploy), ejecutado en GitLab o GitHub Actions, con la regla explícita de que no se despliega si las pruebas fallan. También exige Docker en backend y frontend dentro del proceso, y desplegar en la nube la arquitectura documentada en las fases anteriores. La entrega es el 14 de octubre de 2026, con una ventana de implementación corta que además debe cubrir el contexto de Crédito, el nuevo estilo de notificaciones y las 25 pruebas.

ADR-001 fijó Kubernetes on-premises como plataforma de producción y ADR-013 fijó Docker Compose como entorno de ejecución del MVP, dejando explícito que mantener ambos exige que los manifiestos no se desactualicen. Al iniciar la Fase 3, `deploy/k8s/` contiene un único manifiesto (`consulta-estado-hpa.yaml`: Deployment, Service y HPA de Consulta de Estado). No existen manifiestos para los otros servicios, las bases de datos, RabbitMQ, Redis, el Gateway ni la Web. El sistema tiene 26 servicios en `docker-compose.yml`, tres de ellos bases PostgreSQL con estado, y los servicios dependen del orden de arranque mediante `depends_on` con `condition: service_healthy`.

## Decisión

1. **Orquestación del pipeline: GitHub Actions**, porque el repositorio ya vive en GitHub y no requiere infraestructura adicional.
   - `ci.yml` ejecuta build (compilación en Release con advertencias como error), pruebas unitarias y pruebas de integración (Testcontainers sobre el Docker del runner). Se dispara en pull requests hacia `develop` y `main` y en pushes a `develop`, `release/**` y `hotfix/**`.
   - `cd.yml` se dispara con el merge a `main` (el release de git-flow) o a mano. Ejecuta el CI como primer job; los jobs de imágenes y de despliegue dependen de él mediante `needs`, de modo que un fallo de build o de pruebas impide publicar y desplegar.
2. **Docker en backend y frontend:** cada servicio .NET, la Web y los dos mocks Node se construyen con su Dockerfile en una matriz de 20 imágenes y se publican en GitHub Container Registry (`ghcr.io`) con el tag del commit (7 caracteres) y `latest`.
3. **Destino de despliegue: una instancia AWS EC2** (Ubuntu 24.04, `m7i-flex.large`, IP elástica) que ejecuta el mismo `docker-compose.yml` del MVP más un override `docker-compose.prod.yml` que reemplaza `build` por `image` (GHCR) y agrega `restart: unless-stopped`. El despliegue se hace por SSH: se copia el código del commit, se escribe el `.env` desde un secret de GitHub, se descargan las imágenes y se ejecuta `docker compose up -d --no-build`, esperando a que el Gateway y la Web estén sanos.
4. **Pruebas E2E posteriores al despliegue:** al terminar el despliegue se ejecutan las cinco pruebas E2E de Playwright contra la Web desplegada. Su resultado no revierte el despliegue; deja el run en rojo para revisión.
5. **Kubernetes se mantiene como plataforma de producción** según ADR-001, sin cambios. El manifiesto de `deploy/k8s/` sigue siendo la evidencia documental de esa decisión.

## Alternativas consideradas

Se evaluaron cinco opciones. (1) Kubernetes administrado (GKE o EKS): es la más fiel a la arquitectura documentada, pero exigía escribir y depurar manifiestos para unos 25 servicios, volúmenes persistentes para PostgreSQL, RabbitMQ y Redis, el reemplazo de `depends_on` por sondas de readiness, los jobs de siembra y un Ingress, además de un costo mensual por clúster, todo dentro de la ventana de entrega; se descartó por tiempo y costo, no por falta de mérito técnico. (2) k3s sobre la misma VM: reduce el costo del clúster pero conserva todo el trabajo de manifiestos y de depuración, por lo que ahorra poco frente a la opción 1. (3) Servicios administrados de AWS (ECS/Fargate con RDS y Amazon MQ): cambia la plataforma y obliga a reescribir la configuración de cada servicio; se descartó por el mismo motivo de tiempo y porque se aleja de lo ya documentado en ADR-013. (4) GitLab CI: cumple el enunciado, pero obligaba a migrar o duplicar el repositorio. (5) Construir las imágenes en la propia VM en lugar de usar un registro: elimina GHCR pero convierte cada despliegue en una compilación de 20 imágenes sobre una máquina de 8 GiB y deja sin artefacto versionado al que volver; se descartó. Se eligió GitHub Actions con GHCR y una VM con Compose porque es la única combinación que cumple todos los requisitos del enunciado reutilizando el entorno ya probado y dejando artefactos versionados por commit.

## Consecuencias

### Positivas
- El despliegue depende solo de artefactos construidos por el pipeline: cada release queda identificado por el tag de imagen del commit, y volver a una versión anterior es cambiar `IMAGE_TAG` y repetir `up -d`.
- La regla "no se despliega si las pruebas fallan" queda expresada en el grafo de dependencias del workflow, no como convención.
- La topología de mensajería, el aislamiento de bases por contexto y la configuración de los servicios son idénticas a las del MVP, así que no hay una segunda definición del sistema que mantener alineada para este entorno.
- Los secretos (llave SSH y contenido del `.env`) viven en GitHub Secrets y nunca en el repositorio.

### Negativas
- Un solo nodo es un punto único de falla y no ofrece alta disponibilidad. El HPA y el escalado horizontal de ADR-001 siguen sin demostrarse en ejecución, solo como evidencia documental.
- Las bases de datos corren en contenedores con volúmenes locales de la VM, sin respaldo automático.
- Una instancia de 8 GiB ejecuta los 26 servicios con poco margen; se mitiga con swap, pero las pruebas de carga y estrés se deben ejecutar desde fuera de la instancia y sus resultados se interpretan con ese límite.
- Los puertos de infraestructura (RabbitMQ, Redis, Prometheus, mocks) siguen publicados por el compose base y quedan protegidos solo por el Security Group de AWS.
- La prueba E2E-06 no se ejecuta en el pipeline porque depende de un endpoint interno de Crédito no expuesto; se corre de forma manual contra el entorno local.
- Se mantienen dos formas de desplegar (Compose en la nube, Kubernetes como destino de producción), con el mismo riesgo de desalineación que señaló ADR-013.

## Preocupaciones y evidencia que atiende

PRE-17, PRE-19. Evidencia: RES-14, RES-15. Complementa ADR-001 y ADR-013 sin reemplazarlos.
