# ADR-007: Capa anticorrupción para integrar el ERP AS/400 sin modificarlo

**Estado:** Aceptada
**Responsable:** Francisco Gerardo Castillo Sagastume (202300653)
**Fecha:** 2026-08-24

## Contexto

El ERP-Core es la plataforma contable-financiera de MegaStore, construida sobre AS/400 en la década de 1990 (EV-13). La Junta Directiva determinó que no será reemplazado ni modificado dentro del horizonte de esta iniciativa (EV-14, ratificado en EV-40), y Carlos Méndez, Gerente de TI, tiene poder de rechazo explícito sobre cualquier propuesta que lo module (EV-37). Su modelo de datos y semántica son propios y no deben contaminar el dominio nuevo de SGO-Logic.

## Decisión

Se introduce una capa anticorrupción (Anti-Corruption Layer) dedicada a la integración con el ERP, que traduce el modelo de eventos de venta de SGO-Logic al formato que el ERP admite (archivos CSV o XML por lotes), sin que el ERP conozca ni participe del modelo de dominio nuevo (ver `RES-01` en `DDA_07_Drivers_de_Restriccion_RES.md` y `CU-13` en `DDA_04_Drivers_Funcionales_CDU.md`).

## Alternativas consideradas

Se evaluaron cuatro opciones: (1) capa anticorrupción dedicada que traduce el modelo de SGO-Logic al formato del ERP; (2) que cada contexto genere directamente el formato que el ERP admite, descartada porque esparce la lógica del legacy por todo el sistema y lo acopla al AS/400; (3) modificar el ERP para que acepte el modelo nuevo, descartada de raíz por la restricción de la Junta de no tocarlo (EV-14); y (4) una base de datos intermedia compartida entre SGO-Logic y el ERP, descartada por crear un punto de acoplamiento de datos y violar el principio de que cada contexto es dueño de los suyos. Se eligió la capa anticorrupción dedicada porque concentra en un único punto toda la traducción hacia el legacy, protege al dominio nuevo de la semántica del AS/400 y absorbe cualquier cambio futuro del ERP sin propagarlo al resto del sistema.

## Consecuencias

### Positivas
- El dominio nuevo queda protegido de la semántica y las limitaciones técnicas del sistema legacy.
- Cualquier cambio futuro en el ERP (o su eventual reemplazo, fuera del horizonte actual) se absorbe en un único punto de traducción sin afectar al resto del sistema.

### Negativas
- La capa anticorrupción es un componente adicional que mantener; cualquier cambio en el formato que admite el ERP obliga a modificarla.
- El ERP sigue siendo, mientras exista, una dependencia dura y no negociable del ciclo de cierre contable de la organización.

## Preocupaciones y evidencia que atiende

PRE-11, PRE-16. Evidencia: EV-13, EV-14, EV-40.
