## Context

El backend inicial expone controladores ASP.NET Core net10.0. El documento de arquitectura describe 19 columnas y 6.137 filas en la versión histórica de referencia. Estas cifras sirven para conciliación, no como límites del lector.

## Goals / Non-Goals

Este incremento termina en la API y SQL Server. El tablero y la publicación institucional quedan para otro cambio.

## Decisions

Proponer POST /api/importaciones/graduados (multipart XLSX) y GET /api/importaciones/graduados/{id}. Devolver identificador, estado y contadores de nuevas, vinculadas, pendientes y rechazadas. Proteger carga y detalle por permisos. Resolver columnas por encabezado y documentar el mapeo real; respetar formatos de Excel en documentos y fechas. Ante identidad ambigua, conservar el original para revisión. Un reporte Excel corregible conserva fila, campo y motivo. El ODS sirve para conciliación agregada.

## Risks / Trade-offs

Ejecutar integración y E2E contra SQL Server real en Docker o una instancia dedicada con BD desechable por ejecución. Aislar el servidor cuando se aplique 001_inicial.sql, que selecciona SysGraduados por nombre. Usar un XLSX sintético con la estructura del Excel de graduados; mantener el archivo institucional fuera del repositorio.
