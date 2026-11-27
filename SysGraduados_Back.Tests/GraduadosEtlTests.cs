using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SysGraduados_Back.Importaciones;

namespace SysGraduados_Back.Tests;

public sealed class GraduadosEtlTests
{
    private const string Ruta = "/api/importaciones/graduados";

    [Fact]
    public void ExtraeHojaConsolidadaYConservaTexto()
    {
        using var libro = Libro(NuevaFila("T1", "31/01/22"));
        var fila = Assert.Single(ExcelGraduados.Leer(libro, "CR"));
        Assert.Equal(2, fila.numero_fila);
        Assert.Equal("0-0123-4567", fila.datos_originales["CEDULA"]);
        Assert.Null(fila.detalle_incidencias);
        var json = System.Text.Json.JsonSerializer.SerializeToElement(fila.datos_normalizados);
        Assert.Equal("001234567", json.GetProperty("numero_identificacion").GetString());
        Assert.Equal("2022-01-31", json.GetProperty("fecha_juramentacion").GetString());
        Assert.Equal("88881111", Assert.Single(json.GetProperty("telefonos").EnumerateArray()).GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, json.GetProperty("correo_electronico").ValueKind);
    }

    [Fact]
    public void ReportaFechaInvalidaSinPerderLaFila()
    {
        using var libro = Libro(NuevaFila("T1", "31/02/2022"));
        var fila = Assert.Single(ExcelGraduados.Leer(libro, "CR"));
        Assert.Contains(fila.detalle_incidencias!, e => e.campo == "FECHA_JURAMENTACION");
        Assert.Equal("31/02/2022", fila.datos_originales["FECHA_JURAMENTACION"]);
    }

    [Fact]
    public async Task ApiYSqlServerProcesanReintentosEIncidencias()
    {
        var config = Environment.GetEnvironmentVariable("ETL_TEST_SQL");
        var scripts = Environment.GetEnvironmentVariable("ETL_TEST_SCRIPTS")
            ?? throw new InvalidOperationException("Defina ETL_TEST_SCRIPTS con la carpeta database del repo BD.");
        var nombre = $"SysGraduadosEtlTest_{Guid.NewGuid():N}";
        var cnn = new SqlConnectionStringBuilder(config) { InitialCatalog = "master", Pooling = false };
        await using var admin = new SqlConnection(cnn.ConnectionString);
        await admin.OpenAsync();
        try
        {
            var inicial = (await File.ReadAllTextAsync(Path.Combine(scripts, "001_inicial.sql")))
                .Replace("SysGraduados", nombre, StringComparison.Ordinal);
            foreach (var bloque in Regex.Split(inicial, @"(?im)^\s*GO[ \t]*\r?$"))
                if (!string.IsNullOrWhiteSpace(bloque))
                    await Ejecutar(admin, bloque);

            cnn.InitialCatalog = nombre;
            await using var basePrueba = new SqlConnection(cnn.ConnectionString);
            await basePrueba.OpenAsync();
            foreach (var migracion in new[] { "002_integridad_etl.sql", "003_lotes_etl.sql", "004_confirmar_fila_etl.sql" })
                foreach (var bloque in Regex.Split(await File.ReadAllTextAsync(Path.Combine(scripts, migracion)), @"(?im)^\s*GO[ \t]*\r?$"))
                    if (!string.IsNullOrWhiteSpace(bloque))
                        await Ejecutar(basePrueba, bloque);

            await Ejecutar(basePrueba, """
                INSERT dbo.estado_titulacion(nombre) VALUES(N'VIGENTE');
                INSERT dbo.grado_academico(nombre) VALUES(N'Bachillerato');
                INSERT dbo.carrera(nombre) VALUES(N'Carrera sintética');
                INSERT dbo.unidad_academica(codigo_unidad,nombre) VALUES(N'N',N'Nicoya');
                INSERT dbo.titulo(descripcion_oficial,id_grado,id_carrera) VALUES(N'Título de prueba',1,1);
                INSERT dbo.oferta_titulo(codigo_titulo,codigo_unidad,id_titulo) VALUES(N'T1',N'N',1),(N'T2',N'N',1);
                """);

            using var app = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
                host.ConfigureAppConfiguration((_, builder) => builder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Graduados"] = cnn.ConnectionString,
                    ["Etl:TokenOperador"] = "local-fixture-operator",
                    ["Etl:TokenRevisor"] = "local-fixture-reviewer",
                    ["Etl:NombreRevisor"] = "Revisor de pruebas",
                    ["Etl:TipoIdentificacion"] = "CR"
                })));
            using var cliente = app.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
            using var libro = Libro(NuevaFila("T1", "31/01/2022"), NuevaFila("T2", "31/01/2022"),
                NuevaFila("T1", "31/02/2022"), NuevaFila("T1", "31/01/2022"));
            var datos = libro.ToArray();

            Assert.Equal(HttpStatusCode.Unauthorized, (await Cargar(cliente, datos)).StatusCode);
            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "local-fixture-operator");
            using var archivoSinHoja = new XLWorkbook();
            archivoSinHoja.AddWorksheet("Otra").Cell("A1").Value = "Encabezado";
            using var sinHoja = new MemoryStream();
            archivoSinHoja.SaveAs(sinHoja);
            Assert.Equal(HttpStatusCode.BadRequest, (await Cargar(cliente, sinHoja.ToArray())).StatusCode);

            var respuesta = await Cargar(cliente, datos);
            Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
            var lote = (await respuesta.Content.ReadFromJsonAsync<ResumenLote>())!;
            Assert.Equal((4, 2, 1, 1), (lote.leidas, lote.nuevas, lote.pendientes, lote.rechazadas));
            Assert.Equal(lote, await cliente.GetFromJsonAsync<ResumenLote>($"{Ruta}/{lote.id_lote}"));
            Assert.Equal(lote, await (await Cargar(cliente, datos)).Content.ReadFromJsonAsync<ResumenLote>());

            var exportacion = await cliente.GetByteArrayAsync($"{Ruta}/{lote.id_lote}/incidencias.xlsx");
            using var corregido = new XLWorkbook(new MemoryStream(exportacion));
            corregido.Worksheet(ExcelGraduados.Hoja).Cell("H2").Value = "01/03/2022";
            using var correccion = new MemoryStream();
            corregido.SaveAs(correccion);
            var segundo = (await (await Cargar(cliente, correccion.ToArray())).Content.ReadFromJsonAsync<ResumenLote>())!;
            Assert.Equal(1, segundo.nuevas);
            Assert.NotEqual(lote.id_lote, segundo.id_lote);
            Assert.Equal(1, await Ejecutar(basePrueba, "SELECT COUNT(DISTINCT id_persona) FROM dbo.titulacion"));
            Assert.Equal(3, await Ejecutar(basePrueba, "SELECT COUNT(*) FROM dbo.titulacion"));
            Assert.Equal(3, await Ejecutar(basePrueba, "SELECT COUNT(*) FROM dbo.registro_origen"));
            // Una restricción en el último paso fuerza el rollback de la titulación recién insertada.
            await Ejecutar(basePrueba, """
                ALTER TABLE dbo.registro_origen WITH NOCHECK ADD CONSTRAINT CK_prueba_rollback CHECK (id_titulacion < 0);
                """);
            using var fallo = Libro(NuevaFila("T1", "02/03/2022"));
            var datosFallo = fallo.ToArray();
            using var respuestaFallo = await Cargar(cliente, datosFallo);
            Assert.Equal(HttpStatusCode.InternalServerError, respuestaFallo.StatusCode);
            Assert.Equal(0, await Ejecutar(basePrueba, "SELECT COUNT(*) FROM dbo.titulacion WHERE fecha_juramentacion='20220302'"));
            await Ejecutar(basePrueba, "ALTER TABLE dbo.registro_origen DROP CONSTRAINT CK_prueba_rollback;");
            using var otro = Libro(NuevaFila("T1", "02/03/2022"));
            using var variante = new XLWorkbook(otro);
            variante.Worksheet(ExcelGraduados.Hoja).Cell("S2").Value = "Variante de prueba";
            using var varianteBytes = new MemoryStream();
            variante.SaveAs(varianteBytes);
            var cargas = await Task.WhenAll(Cargar(cliente, datosFallo), Cargar(cliente, datosFallo),
                Cargar(cliente, varianteBytes.ToArray()));
            var lotes = await Task.WhenAll(cargas.Select(r => r.Content.ReadFromJsonAsync<ResumenLote>()));
            Assert.Equal(lotes[0], lotes[1]);
            Assert.NotEqual(lotes[0]!.id_lote, lotes[2]!.id_lote);
            Assert.Equal(1, lotes[0]!.nuevas + lotes[2]!.nuevas);
            Assert.Equal(1, lotes[0]!.pendientes + lotes[2]!.pendientes);

            cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "local-fixture-reviewer");
            var candidatas = await cliente.GetFromJsonAsync<System.Text.Json.JsonElement>($"{Ruta}/{lote.id_lote}/filas/5/candidatas");
            var candidata = Assert.Single(candidatas.EnumerateArray()).GetProperty("id_titulacion").GetInt32();
            var revision = await cliente.PostAsJsonAsync($"{Ruta}/{lote.id_lote}/filas/5/revision",
                new { decision = "vincular", justificacion = "Mismo evento", idCandidata = candidata });
            Assert.Equal(HttpStatusCode.OK, revision.StatusCode);
            Assert.Equal(1, (await revision.Content.ReadFromJsonAsync<ResumenLote>())!.vinculadas);
        }
        finally
        {
            await Ejecutar(admin, $"USE master; IF DB_ID(N'{nombre}') IS NOT NULL DROP DATABASE [{nombre}];");
        }
    }

    [Fact]
    public void LeeElHistoricoCompletoSinPublicarDatosPersonales()
    {
        var ruta = Environment.GetEnvironmentVariable("ETL_TEST_XLSX");
        using var archivo = File.OpenRead(ruta!);
        var filas = ExcelGraduados.Leer(archivo, "CR");
        Assert.Equal(6137, filas.Count);
        Assert.True(filas.Count(fila => fila.detalle_incidencias is null) > 0, "El histórico debe contener filas válidas.");
    }

    private static async Task<HttpResponseMessage> Cargar(HttpClient cliente, byte[] datos)
    {
        using var contenido = new MultipartFormDataContent();
        contenido.Add(new ByteArrayContent(datos), "archivo", "graduados.xlsx");
        return await cliente.PostAsync(Ruta, contenido);
    }

    private static async Task<object?> Ejecutar(SqlConnection conexion, string sentencia)
    {
        await using var comando = new SqlCommand(sentencia, conexion);
        return await comando.ExecuteScalarAsync();
    }

    private static string[] NuevaFila(string titulo, string fecha) =>
    [
        "2022", "0010", "0-0123-4567", "Ejemplo", "", "Persona", "",
        fecha, titulo, "Título", "Bachillerato", "8888-1111", "88881111", "",
        "", "", "", "N", "Nicoya"
    ];

    private static MemoryStream Libro(params string[][] filas)
    {
        using var libro = new XLWorkbook();
        var hoja = libro.AddWorksheet(ExcelGraduados.Hoja);
        for (var i = 0; i < ExcelGraduados.Encabezados.Count; i++)
            hoja.Cell(1, i + 1).Value = ExcelGraduados.Encabezados[i];
        for (var f = 0; f < filas.Length; f++)
            for (var c = 0; c < filas[f].Length; c++)
                hoja.Cell(f + 2, c + 1).Value = filas[f][c];
        libro.AddWorksheet("2022").Cell("A1").Value = "Esta pestaña no forma parte de la carga";
        var stream = new MemoryStream();
        libro.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }
}

internal sealed class EntornoFactAttribute : FactAttribute
{
    public EntornoFactAttribute(string variable)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable)))
            Skip = $"Defina {variable} para ejecutar esta prueba.";
    }
}
