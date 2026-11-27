using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace SysGraduados_Back.Importaciones;

public sealed record ResumenLote(int id_lote, string estado, int leidas, int nuevas,
    int vinculadas, int pendientes, int rechazadas, int sin_procesar);

public sealed class EtlSql(IConfiguration configuracion)
{
    public async Task<SqlConnection> Conectar(CancellationToken cancelacion)
    {
        var conexion = new SqlConnection(configuracion.GetConnectionString("Graduados")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:Graduados."));
        try
        {
            await conexion.OpenAsync(cancelacion);
            return conexion;
        }
        catch
        {
            await conexion.DisposeAsync();
            throw;
        }
    }

    public async Task<int> Importar(string nombre, string hash, IReadOnlyList<FilaGraduado> filas, CancellationToken ct)
    {
        await using var conexion = await Conectar(ct);
        await using var registro = Procedimiento(conexion, "dbo.etl_registrar_lote");
        registro.Parameters.Add("@nombre_archivo", SqlDbType.NVarChar, 256).Value = nombre;
        registro.Parameters.Add("@hoja", SqlDbType.NVarChar, 256).Value = ExcelGraduados.Hoja;
        registro.Parameters.Add("@hash_archivo", SqlDbType.NVarChar, 256).Value = hash;
        registro.Parameters.Add("@filas", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(filas);
        var id = (int)(await registro.ExecuteScalarAsync(ct))!;

        var pendientes = new List<int>();
        await using (var comando = new SqlCommand("""
            SELECT numero_fila FROM dbo.stg_titulacion
            WHERE id_lote=@lote AND estado_validacion=N'por_procesar'
            ORDER BY numero_fila
            """, conexion))
        {
            comando.Parameters.Add("@lote", SqlDbType.Int).Value = id;
            await using var lector = await comando.ExecuteReaderAsync(ct);
            while (await lector.ReadAsync(ct))
                pendientes.Add(lector.GetInt32(0));
        }

        await using var confirmar = Procedimiento(conexion, "dbo.etl_confirmar_fila");
        confirmar.Parameters.Add("@id_lote", SqlDbType.Int).Value = id;
        var numero = confirmar.Parameters.Add("@numero_fila", SqlDbType.Int);
        foreach (var fila in pendientes)
        {
            numero.Value = fila;
            await confirmar.ExecuteNonQueryAsync(ct);
        }

        await using var finalizar = new SqlCommand("""
            UPDATE dbo.lote_importacion SET estado=N'procesado' WHERE id_lote=@lote
            AND NOT EXISTS (SELECT 1 FROM dbo.stg_titulacion
                            WHERE id_lote=@lote AND estado_validacion=N'por_procesar');
            """, conexion);
        finalizar.Parameters.Add("@lote", SqlDbType.Int).Value = id;
        await finalizar.ExecuteNonQueryAsync(ct);
        return id;
    }

    public async Task<ResumenLote?> Resumen(int id, CancellationToken ct)
    {
        await using var conexion = await Conectar(ct);
        await using var comando = new SqlCommand("""
            SELECT id_lote, estado, leidas, nuevas, vinculadas, pendientes, rechazadas, sin_procesar
            FROM dbo.etl_resumen_lote WHERE id_lote=@lote
            """, conexion);
        comando.Parameters.Add("@lote", SqlDbType.Int).Value = id;
        await using var lector = await comando.ExecuteReaderAsync(ct);
        return await lector.ReadAsync(ct)
            ? new(lector.GetInt32(0), lector.GetString(1), lector.GetInt32(2), lector.GetInt32(3),
                lector.GetInt32(4), lector.GetInt32(5), lector.GetInt32(6), lector.GetInt32(7))
            : null;
    }

    public static SqlCommand Procedimiento(SqlConnection conexion, string nombre) =>
        new(nombre, conexion) { CommandType = CommandType.StoredProcedure };
}
