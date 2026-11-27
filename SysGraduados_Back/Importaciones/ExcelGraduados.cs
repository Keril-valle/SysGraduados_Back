using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace SysGraduados_Back.Importaciones;

public sealed record Incidencia(string campo, string motivo);
public sealed record FilaGraduado(int numero_fila, int? anio_en_archivo,
    Dictionary<string, string> datos_originales, object? datos_normalizados,
    IReadOnlyList<Incidencia>? detalle_incidencias);

public static class ExcelGraduados
{
    public const string Hoja = "Exportar Hoja de Trabajo";
    public static IReadOnlyList<string> Encabezados { get; } = Array.AsReadOnly(new[]
    {
        "AÑO", "CARNET", "CEDULA", "PRIMER_APELLIDO", "SEGUNDO_APELLIDO", "NOMBRE", "SEXO",
        "FECHA_JURAMENTACION", "CODIGO_TITULO", "DESCRIPCION_TITULO", "DESCRIPCION_GRADO",
        "TELEFONO", "TELEFONO2", "TELEFONO3", "E_MAIL", "CODIGO_PAIS", "DESCRIPCION_PAIS",
        "CODIGO_UNIDAD", "SUBSEDE"
    });
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-CR");

    public static IReadOnlyList<FilaGraduado> Leer(Stream archivo, string tipoIdentificacion)
    {
        using var libro = new XLWorkbook(archivo);
        if (!libro.TryGetWorksheet(Hoja, out var hoja))
            throw new InvalidDataException($"Falta la hoja {Hoja}.");

        var columnas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var celda in hoja.Row(1).CellsUsed())
        {
            var encabezado = celda.GetString().Trim();
            if (encabezado.Length > 0 && !columnas.TryAdd(encabezado, celda.Address.ColumnNumber))
                throw new InvalidDataException($"Encabezado repetido: {encabezado}.");
        }
        var faltantes = Encabezados.Where(campo => !columnas.ContainsKey(campo)).ToArray();
        if (faltantes.Length > 0)
            throw new InvalidDataException($"Faltan columnas: {string.Join(", ", faltantes)}.");

        var resultado = new List<FilaGraduado>();
        foreach (var fila in hoja.RowsUsed().Where(fila => fila.RowNumber() > 1))
        {
            var originales = Encabezados.ToDictionary(campo => campo,
                campo => fila.Cell(columnas[campo]).GetFormattedString(Cultura));
            if (originales.Values.All(string.IsNullOrWhiteSpace))
                continue;

            var incidencias = new List<Incidencia>();
            string? Campo(string nombre, int maximo, bool requerido = false)
            {
                var texto = originales[nombre].Trim();
                if ((requerido && texto.Length == 0) || texto.Length > maximo)
                    incidencias.Add(new(nombre, $"Debe contener {(requerido ? 1 : 0)} a {maximo} caracteres."));
                return texto.Length == 0 ? null : texto;
            }

            int? anio = int.TryParse(originales["AÑO"], NumberStyles.Integer, Cultura, out var valorAnio)
                ? valorAnio : null;
            var celdaFecha = fila.Cell(columnas["FECHA_JURAMENTACION"]);
            var fecha = LeerFecha(celdaFecha, originales["FECHA_JURAMENTACION"], anio);
            if (fecha is null)
                incidencias.Add(new("FECHA_JURAMENTACION", "Fecha ausente o inválida para el formato de Costa Rica."));
            if (anio is null || fecha is not null && anio != fecha.Value.Year)
                incidencias.Add(new("AÑO", "Debe coincidir con el año de la juramentación."));

            var correo = Campo("E_MAIL", 256);
            if (correo is not null && (!MailAddress.TryCreate(correo, out var direccion) || direccion.Address != correo))
                incidencias.Add(new("E_MAIL", "Correo inválido."));

            var telefonos = new HashSet<string>(StringComparer.Ordinal);
            foreach (var columna in new[] { "TELEFONO", "TELEFONO2", "TELEFONO3" })
            {
                var valor = Campo(columna, 64);
                if (valor is null)
                    continue;
                var telefono = Regex.Replace(valor, @"[\s().-]", "");
                if (!Regex.IsMatch(telefono, @"^\+?[0-9]{1,63}$"))
                    incidencias.Add(new(columna, "Teléfono inválido."));
                else
                    telefonos.Add(telefono);
            }

            var cedula = Campo("CEDULA", 64, true);
            var numero = cedula is null ? null : Regex.Replace(cedula, @"[\s-]", "");
            if (numero is not null && !Regex.IsMatch(numero, @"^[0-9]{9}$"))
                incidencias.Add(new("CEDULA", "El tipo de identificación requiere clasificación autorizada."));

            var normalizados = new
            {
                tipo_identificacion = tipoIdentificacion,
                numero_identificacion = numero,
                carnet = Campo("CARNET", 256),
                primer_apellido = Campo("PRIMER_APELLIDO", 256, true),
                segundo_apellido = Campo("SEGUNDO_APELLIDO", 256),
                nombre = Campo("NOMBRE", 256, true),
                sexo_registrado = Campo("SEXO", 256),
                fecha_juramentacion = fecha,
                codigo_titulo = Campo("CODIGO_TITULO", 64, true),
                codigo_unidad = Campo("CODIGO_UNIDAD", 64, true),
                codigo_pais = Campo("CODIGO_PAIS", 64),
                correo_electronico = correo,
                telefonos
            };
            resultado.Add(new(fila.RowNumber(), anio, originales, normalizados,
                incidencias.Count == 0 ? null : incidencias));
        }

        return resultado;
    }

    private static DateOnly? LeerFecha(IXLCell celda, string original, int? anio)
    {
        if (celda.DataType == XLDataType.DateTime)
            return DateOnly.FromDateTime(celda.GetDateTime());
        var texto = original.Trim();
        // La hoja histórica usa dd/MM/yy como texto. AÑO fija el siglo.
        var corta = Regex.Match(texto, @"^(\d{1,2}/\d{1,2}/)(\d{2})$");
        if (corta.Success && anio is >= 1 and <= 9999
            && int.Parse(corta.Groups[2].Value, CultureInfo.InvariantCulture) == anio.Value % 100)
            texto = corta.Groups[1].Value + anio.Value.ToString("0000", CultureInfo.InvariantCulture);

        return DateTime.TryParseExact(texto,
            ["d/M/yyyy", "yyyy-MM-dd", "d-M-yyyy", "d/M/yyyy H:mm:ss", "yyyy-MM-dd HH:mm:ss"],
            Cultura, DateTimeStyles.None, out var fecha) ? DateOnly.FromDateTime(fecha) : null;
    }
}
