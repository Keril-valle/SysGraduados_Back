# Importación histórica de Graduados

La entrada es **Titulaciones Chorotega 2010-2026.xlsx**, hoja **Exportar Hoja de Trabajo**. La fila 1 contiene 19 encabezados verificados. El lector conserva la fila física, los valores formateados y las incidencias. El ODS sirve para conciliación agregada.

- Persona: CEDULA, CARNET, NOMBRE, PRIMER_APELLIDO, SEGUNDO_APELLIDO, SEXO, E_MAIL, CODIGO_PAIS y TELEFONO, TELEFONO2, TELEFONO3. Cédula, nombre y primer apellido son obligatorios. El tipo se configura para cédulas de nueve dígitos, con o sin guiones, porque no existe en el Excel. Los demás formatos generan una incidencia para clasificación autorizada.
- Evento: AÑO, FECHA_JURAMENTACION, CODIGO_TITULO y CODIGO_UNIDAD son obligatorios. El año debe coincidir con la fecha.
- DESCRIPCION_TITULO, DESCRIPCION_GRADO, DESCRIPCION_PAIS y SUBSEDE se conservan para cotejar con los catálogos autorizados.

## Configuración

Aplicá los scripts 001–004 de SysGraduados_bd en orden. En una instalación existente que ya tiene 001, aplicá a partir de 002. Cargá los catálogos de oferta, país y estado con datos autorizados. La creación de titulaciones requiere el estado VIGENTE.

Configurá mediante secretos o variables de entorno ConnectionStrings__Graduados, Etl__TipoIdentificacion y Etl__TokenOperador. Para habilitar revisiones configurá Etl__TokenRevisor y Etl__NombreRevisor. Usá tokens distintos por permiso. Los endpoints reciben Authorization: Bearer y la carga usa multipart con el campo archivo.

La API devuelve los contadores del lote, exporta un XLSX corregible de filas rechazadas y permite consultar las candidatas de una revisión por fila. Corregí y subí el archivo exportado para procesar solo las filas rechazadas. Una coincidencia queda pendiente hasta que el revisor la vincule o autorice un nuevo evento con justificación.

## Pruebas

Ejecutá dotnet test desde el repositorio del backend. Para activar el E2E, configurá ETL_TEST_SQL con la conexión de una instancia de desarrollo que permita crear y eliminar bases de pruebas, y ETL_TEST_SCRIPTS con la carpeta database de SysGraduados_bd. Sirve SQL Server LocalDB o SQL Server en Docker. El E2E crea y elimina su propia base y utiliza un Excel sintético. ETL_TEST_XLSX permite contrastar la extracción del histórico en solo lectura.
