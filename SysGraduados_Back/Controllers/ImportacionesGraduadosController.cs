using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SysGraduados_Back.Importaciones;

namespace SysGraduados_Back.Controllers;

[ApiController]
[Route("api/importaciones/graduados")]
public sealed class ImportacionesGraduadosController(EtlSql sql, IConfiguration configuracion) : ControllerBase
{
    private const int MaximoArchivo = 20 * 1024 * 1024;

    [HttpPost]
    [RequestSizeLimit(MaximoArchivo + 65536)]
    public async Task<IActionResult> Cargar(IFormFile archivo, CancellationToken ct)
    {
        if (!Autorizado("TokenOperador"))
            return Unauthorized();

        var tipo = configuracion["Etl:TipoIdentificacion"]?.Trim();
        if (string.IsNullOrEmpty(tipo) || tipo.Length > 64)
            return Problem("Configure Etl:TipoIdentificacion con el criterio autorizado para este histórico.",
                statusCode: StatusCodes.Status503ServiceUnavailable);

        var nombre = Path.GetFileName(archivo.FileName);
        if (archivo.Length is <= 0 or > MaximoArchivo || nombre.Length > 256 ||
            !nombre.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Se requiere un archivo XLSX de hasta 20 MB." });

        IReadOnlyList<FilaGraduado> filas;
        byte[] hash;
        using (var contenido = new MemoryStream())
        {
            await archivo.CopyToAsync(contenido, ct);
            hash = SHA256.HashData(contenido.GetBuffer().AsSpan(0, (int)contenido.Length));
            contenido.Position = 0;
            try
            {
                filas = ExcelGraduados.Leer(contenido, tipo);
            }
            catch (InvalidDataException error)
            {
                return BadRequest(new { error = error.Message });
            }
            catch (Exception error) when (error is DocumentFormat.OpenXml.Packaging.OpenXmlPackageException
                or System.IO.IOException or XmlException or ArgumentException or FormatException or InvalidOperationException)
            {
                return BadRequest(new { error = "No fue posible leer el archivo XLSX." });
            }
        }

        var id = await sql.Importar(nombre, Convert.ToHexStringLower(hash), filas, ct);
        return CreatedAtAction(nameof(Consultar), new { id }, await sql.Resumen(id, ct));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Consultar(int id, CancellationToken ct)
    {
        if (!Autorizado("TokenOperador"))
            return Unauthorized();

        var lote = await sql.Resumen(id, ct);
        return lote is null ? NotFound() : Ok(lote);
    }

    [HttpGet("{id:int}/incidencias.xlsx")]
    public async Task<IActionResult> Incidencias(int id, CancellationToken ct)
    {
        if (!Autorizado("TokenOperador"))
            return Unauthorized();
        if (await sql.Resumen(id, ct) is null)
            return NotFound();

        using var libro = new XLWorkbook();
        var datos = libro.AddWorksheet(ExcelGraduados.Hoja);
        var incidencias = libro.AddWorksheet("Incidencias");
        for (var i = 0; i < ExcelGraduados.Encabezados.Count; i++)
            datos.Cell(1, i + 1).Value = ExcelGraduados.Encabezados[i];
        incidencias.Cell(1, 1).Value = "Fila en el archivo original";
        incidencias.Cell(1, 2).Value = "Campo";
        incidencias.Cell(1, 3).Value = "Motivo";

        await using var conexion = await sql.Conectar(ct);
        await using var consulta = new SqlCommand("""
            SELECT numero_fila, datos_originales, detalle_incidencias FROM dbo.stg_titulacion
            WHERE id_lote=@id AND estado_validacion=N'rechazada' ORDER BY numero_fila
            """, conexion);
        consulta.Parameters.Add("@id", SqlDbType.Int).Value = id;
        await using var lector = await consulta.ExecuteReaderAsync(ct);
        var filaDatos = 2;
        var filaIncidencia = 2;
        while (await lector.ReadAsync(ct))
        {
            var originales = JsonSerializer.Deserialize<Dictionary<string, string>>(lector.GetString(1))!;
            for (var i = 0; i < ExcelGraduados.Encabezados.Count; i++)
                if (originales.TryGetValue(ExcelGraduados.Encabezados[i], out var original))
                    datos.Cell(filaDatos, i + 1).Value = original;
            filaDatos++;

            foreach (var incidencia in JsonSerializer.Deserialize<List<Incidencia>>(lector.GetString(2))!)
            {
                incidencias.Cell(filaIncidencia, 1).Value = lector.GetInt32(0);
                incidencias.Cell(filaIncidencia, 2).Value = incidencia.campo;
                incidencias.Cell(filaIncidencia, 3).Value = incidencia.motivo;
                filaIncidencia++;
            }
        }

        using var exportacion = new MemoryStream();
        libro.SaveAs(exportacion);
        return File(exportacion.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"incidencias-lote-{id}.xlsx");
    }

    [HttpGet("{id:int}/filas/{fila:int}/candidatas")]
    public async Task<IActionResult> Candidatas(int id, int fila, CancellationToken ct)
    {
        if (!Autorizado("TokenRevisor"))
            return Unauthorized();

        await using var conexion = await sql.Conectar(ct);
        await using var comando = new SqlCommand("""
            SELECT t.id_titulacion, t.codigo_titulo, t.fecha_juramentacion
            FROM dbo.stg_titulacion AS s
            JOIN dbo.revision_duplicado AS r ON r.id_staging=s.id_staging AND r.decision IS NULL
            JOIN dbo.revision_coincidencia AS c ON c.id_revision=r.id_revision
            JOIN dbo.titulacion AS t ON t.id_titulacion=c.id_titulacion_candidata
            WHERE s.id_lote=@id AND s.numero_fila=@fila AND s.estado_validacion=N'pendiente'
            ORDER BY t.id_titulacion
            """, conexion);
        comando.Parameters.Add("@id", SqlDbType.Int).Value = id;
        comando.Parameters.Add("@fila", SqlDbType.Int).Value = fila;
        await using var lector = await comando.ExecuteReaderAsync(ct);
        var candidatas = new List<object>();
        while (await lector.ReadAsync(ct))
            candidatas.Add(new { id_titulacion = lector.GetInt32(0), codigo_titulo = lector.GetString(1),
                fecha_juramentacion = DateOnly.FromDateTime(lector.GetDateTime(2)) });
        return candidatas.Count == 0 ? NotFound() : Ok(candidatas);
    }

    [HttpPost("{id:int}/filas/{fila:int}/revision")]
    public async Task<IActionResult> Revisar(int id, int fila, Revision entrada, CancellationToken ct)
    {
        if (!Autorizado("TokenRevisor"))
            return Unauthorized();
        var revisor = configuracion["Etl:NombreRevisor"];
        if (string.IsNullOrWhiteSpace(revisor) || revisor.Length > 256)
            return Problem("Configure Etl:NombreRevisor.", statusCode: StatusCodes.Status503ServiceUnavailable);
        if (entrada.Decision is not ("vincular" or "crear") || string.IsNullOrWhiteSpace(entrada.Justificacion))
            return BadRequest(new { error = "Indique una decisión válida y su justificación." });

        await using var conexion = await sql.Conectar(ct);
        await using var comando = EtlSql.Procedimiento(conexion, "dbo.etl_confirmar_fila");
        comando.Parameters.Add("@id_lote", SqlDbType.Int).Value = id;
        comando.Parameters.Add("@numero_fila", SqlDbType.Int).Value = fila;
        comando.Parameters.Add("@decision", SqlDbType.NVarChar, 16).Value = entrada.Decision;
        comando.Parameters.Add("@revisado_por", SqlDbType.NVarChar, 256).Value = revisor;
        comando.Parameters.Add("@justificacion", SqlDbType.NVarChar, -1).Value = entrada.Justificacion;
        comando.Parameters.Add("@id_candidata", SqlDbType.Int).Value = (object?)entrada.IdCandidata ?? DBNull.Value;
        try
        {
            await comando.ExecuteNonQueryAsync(ct);
        }
        catch (SqlException error) when (error.Number is >= 51004 and <= 51008)
        {
            return error.Number <= 51005 ? NotFound() : Conflict(new { error = error.Message });
        }
        return Ok(await sql.Resumen(id, ct));
    }

    public sealed record Revision(string Decision, string Justificacion, int? IdCandidata);

    private bool Autorizado(string clave)
    {
        var esperado = configuracion[$"Etl:{clave}"];
        var recibido = Request.Headers.Authorization.ToString();
        return !string.IsNullOrWhiteSpace(esperado) && recibido.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            && CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(esperado)),
                SHA256.HashData(Encoding.UTF8.GetBytes(recibido[7..])));
    }
}
