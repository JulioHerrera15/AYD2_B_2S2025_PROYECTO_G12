# ADR-013: Docker Compose como entorno de ejecución del MVP

**Estado:** Aceptada
**Responsable:** Angel Martinez (202300410)
**Fecha:** 2026-09-11

## Contexto

ADR-001 fijó microservicios por contexto delimitado sobre Kubernetes on-premises como plataforma de despliegue (RES-14, RES-15, sección 9.2 del DDA), y esa decisión no cambia: sigue siendo la plataforma de producción. Pero el entregable de la Fase 2 exige un MVP demostrable en una máquina limpia, sin depender de un clúster ya preparado, y el enunciado de esa fase pide explícitamente un archivo `docker-compose.yml` como parte del entregable. Instalar y operar Kubernetes solo para correr una demo local no aporta nada a la evaluación del MVP y sí introduce una barrera de entrada que ningún revisor debería tener que superar para ver el sistema funcionando.

## Decisión

Docker Compose es el entorno de ejecución del MVP: `docker-compose.yml` levanta todos los servicios del Backbone, sus bases de datos por contexto y RabbitMQ en una sola máquina, sin depender de un clúster. La decisión de Kubernetes on-premises de ADR-001 se mantiene vigente para producción, sin modificarse; Docker Compose no la reemplaza, es el entorno del MVP mientras esa decisión de producción no se ejecuta todavía.

## Alternativas consideradas

Se evaluaron tres opciones: (1) Docker Compose como entorno del MVP; (2) desplegar el MVP directamente en Kubernetes local (minikube o kind), descartada por el costo de aprendizaje que ADR-001 ya reconoció como riesgo (RES-12) y por el tiempo que consumiría dentro de la ventana de entrega de la Fase 2, sin aportar nada distinto a lo que ya demuestra Docker Compose para el alcance de un MVP; y (3) ejecutar los servicios directamente sobre el sistema operativo sin contenedores, descartada porque haría la demo dependiente del entorno de quien la corra y no reproducible en una máquina limpia, que es justo lo que el entregable exige. Se eligió Docker Compose porque es el único de los tres que cumple el requisito explícito del enunciado sin gastar la ventana de entrega en infraestructura que el MVP no necesita demostrar.

## Consecuencias

### Positivas
- La topología de mensajería (RabbitMQ, exchanges, colas) y el aislamiento de bases de datos por contexto quedan definidos igual en ambos entornos, así que migrar de Docker Compose a Kubernetes no toca código de aplicación, solo manifiestos de despliegue.
- Cualquier persona con Docker instalado puede levantar el sistema completo en una máquina limpia con un solo comando, sin preparar un clúster.

### Negativas
- El escalado horizontal por HPA que sustenta el pico de 8x de Black Friday (ADR-001) no se demuestra en el MVP: Docker Compose no tiene equivalente de HPA. Esa capacidad queda como evidencia documental de la decisión de Kubernetes, no como algo ejecutado en la demo.
- Mantener dos formas de desplegar (Compose para el MVP, Kubernetes para producción) exige que ambas queden alineadas conforme el sistema evolucione, o el manifiesto que no se usa en el día a día se queda desactualizado.

## Preocupaciones y evidencia que atiende

PRE-17, PRE-19. Evidencia: RES-14, RES-15.
