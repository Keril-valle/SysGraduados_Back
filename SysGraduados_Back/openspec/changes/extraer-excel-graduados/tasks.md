## 1. Implementación y verificación

- [x] 1.1 Contrastar los 19 encabezados de la hoja consolidada con el archivo autorizado y fijar mapeo, obligatoriedad y ejemplos sintéticos.
- [x] 1.2 Implementar extracción, normalización y diagnóstico por fila; probar unitariamente con xUnit fechas, ceros iniciales, teléfonos repetidos y valores ausentes.
- [x] 1.3 Implementar carga HTTP autorizada, consulta del lote y exportación de incidencias sobre el contrato de persistir-etl-graduados.
- [x] 1.4 Crear prueba E2E con API de prueba y SQL Server real: subir XLSX con dos filas válidas de una persona, una inválida y una coincidencia; verificar dos títulos, una persona, una rechazada y una pendiente.
- [x] 1.5 Repetir archivo, consultar lote, corregir la fila inválida y verificar solo un alta adicional. Probar hoja ausente y acceso sin permiso. Documentar dotnet test y conexión de pruebas.
