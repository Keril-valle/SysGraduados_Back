## Purpose

Extraer y validar el histórico de graduados de la Sede Regional Chorotega con trazabilidad hasta la celda de origen.

## ADDED Requirements

### Requirement: Seleccionar la fuente
El sistema SHALL garantizar el siguiente comportamiento.

#### Scenario: Seleccionar la fuente
- **WHEN** se recibe Titulaciones Chorotega 2010-2026.xlsx con hoja consolidada y pestañas anuales
- **THEN** solo se extraen las filas de datos de Exportar Hoja de Trabajo, identificadas por número físico de fila.

### Requirement: Contrato del libro
El sistema SHALL garantizar el siguiente comportamiento.

#### Scenario: Contrato del libro
- **WHEN** falta la hoja consolidada o un encabezado obligatorio, o el archivo es ilegible
- **THEN** la API informa el motivo y no realiza altas académicas.

### Requirement: Normalización auditable
El sistema SHALL garantizar el siguiente comportamiento.

#### Scenario: Normalización auditable
- **WHEN** una cédula textual comienza con cero, dos teléfonos coinciden y falta el correo
- **THEN** se conserva la identificación como texto, se deduplica el teléfono y se admite correo ausente; staging conserva valores originales y normalizados.

### Requirement: Carga parcial
El sistema SHALL garantizar el siguiente comportamiento.

#### Scenario: Carga parcial
- **WHEN** una fila tiene fecha ambigua o una oferta desconocida junto a otra fila válida
- **THEN** se procesa la válida y se reportan fila, campo y motivo para corregir la rechazada.

### Requirement: Consulta y corrección
El sistema SHALL garantizar el siguiente comportamiento.

#### Scenario: Consulta y corrección
- **WHEN** un operador autorizado carga un libro corregido tras un rechazo
- **THEN** puede consultar el resultado del nuevo lote y reconciliar sus totales sin duplicar las titulaciones ya confirmadas.
