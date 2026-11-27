IF DB_ID(N'SysGraduados') IS NULL EXEC(N'CREATE DATABASE SysGraduados');
GO
USE SysGraduados;
GO
SET XACT_ABORT ON;
BEGIN TRANSACTION;

CREATE TABLE dbo.pais (
    codigo_pais nvarchar(64) NOT NULL,
    nombre nvarchar(256) NOT NULL,
    PRIMARY KEY (codigo_pais)
);

CREATE TABLE dbo.grado_academico (
    id_grado int IDENTITY(1,1) NOT NULL,
    nombre nvarchar(256) NOT NULL UNIQUE,
    PRIMARY KEY (id_grado)
);

CREATE TABLE dbo.carrera (
    id_carrera int IDENTITY(1,1) NOT NULL,
    nombre nvarchar(256) NOT NULL,
    PRIMARY KEY (id_carrera)
);

CREATE TABLE dbo.estado_titulacion (
    id_estado int IDENTITY(1,1) NOT NULL,
    nombre nvarchar(256) NOT NULL UNIQUE,
    PRIMARY KEY (id_estado)
);

CREATE TABLE dbo.unidad_academica (
    codigo_unidad nvarchar(64) NOT NULL,
    nombre nvarchar(256) NOT NULL,
    codigo_unidad_padre nvarchar(64) NULL REFERENCES dbo.unidad_academica(codigo_unidad),
    PRIMARY KEY (codigo_unidad)
);

CREATE TABLE dbo.titulo (
    id_titulo int IDENTITY(1,1) NOT NULL,
    descripcion_oficial nvarchar(256) NOT NULL,
    id_grado int NOT NULL REFERENCES dbo.grado_academico(id_grado),
    id_carrera int NOT NULL REFERENCES dbo.carrera(id_carrera),
    PRIMARY KEY (id_titulo)
);

CREATE TABLE dbo.oferta_titulo (
    codigo_titulo nvarchar(64) NOT NULL,
    codigo_unidad nvarchar(64) NOT NULL REFERENCES dbo.unidad_academica(codigo_unidad),
    id_titulo int NOT NULL REFERENCES dbo.titulo(id_titulo),
    PRIMARY KEY (codigo_titulo)
);

CREATE TABLE dbo.persona (
    id_persona int IDENTITY(1,1) NOT NULL,
    tipo_identificacion nvarchar(64) NOT NULL,
    numero_identificacion nvarchar(64) NOT NULL,
    carnet nvarchar(256) NULL,
    nombre nvarchar(256) NOT NULL,
    primer_apellido nvarchar(256) NOT NULL,
    segundo_apellido nvarchar(256) NULL,
    sexo_registrado nvarchar(256) NULL,
    codigo_pais nvarchar(64) NULL REFERENCES dbo.pais(codigo_pais),
    correo_electronico nvarchar(256) NULL,
    PRIMARY KEY (id_persona),
    UNIQUE (tipo_identificacion, numero_identificacion)
);

CREATE TABLE dbo.persona_telefono (
    id_persona int NOT NULL REFERENCES dbo.persona(id_persona),
    numero nvarchar(64) NOT NULL,
    PRIMARY KEY (id_persona, numero)
);

CREATE TABLE dbo.titulacion (
    id_titulacion int IDENTITY(1,1) NOT NULL,
    id_persona int NOT NULL REFERENCES dbo.persona(id_persona),
    codigo_titulo nvarchar(64) NOT NULL REFERENCES dbo.oferta_titulo(codigo_titulo),
    id_estado int NOT NULL REFERENCES dbo.estado_titulacion(id_estado),
    fecha_juramentacion date NOT NULL,
    origen_registro nvarchar(256) NOT NULL,
    PRIMARY KEY (id_titulacion)
);

CREATE TABLE dbo.lote_importacion (
    id_lote int IDENTITY(1,1) NOT NULL,
    nombre_archivo nvarchar(256) NOT NULL,
    hoja nvarchar(256) NOT NULL,
    hash_archivo nvarchar(256) NOT NULL,
    fecha_importacion datetime2 NOT NULL,
    estado nvarchar(256) NOT NULL,
    PRIMARY KEY (id_lote)
);

CREATE TABLE dbo.stg_titulacion (
    id_staging int IDENTITY(1,1) NOT NULL,
    id_lote int NOT NULL REFERENCES dbo.lote_importacion(id_lote),
    numero_fila int NOT NULL,
    anio_en_archivo int NULL,
    datos_originales nvarchar(max) NOT NULL,
    datos_normalizados nvarchar(max) NULL,
    estado_validacion nvarchar(256) NOT NULL,
    detalle_incidencias nvarchar(max) NULL,
    ultima_validacion datetime2 NULL,
    PRIMARY KEY (id_staging),
    UNIQUE (id_lote, numero_fila),
    CHECK (numero_fila > 0)
);

CREATE TABLE dbo.registro_origen (
    id_staging int NOT NULL REFERENCES dbo.stg_titulacion(id_staging),
    id_titulacion int NOT NULL REFERENCES dbo.titulacion(id_titulacion),
    PRIMARY KEY (id_staging)
);

CREATE TABLE dbo.revision_duplicado (
    id_revision int IDENTITY(1,1) NOT NULL,
    id_staging int NULL REFERENCES dbo.stg_titulacion(id_staging),
    id_titulacion_resultado int NULL REFERENCES dbo.titulacion(id_titulacion),
    datos_solicitud nvarchar(max) NOT NULL,
    decision nvarchar(256) NULL,
    revisado_por nvarchar(256) NULL,
    fecha_revision datetime2 NULL,
    justificacion nvarchar(max) NULL,
    PRIMARY KEY (id_revision)
);

CREATE TABLE dbo.revision_coincidencia (
    id_revision int NOT NULL REFERENCES dbo.revision_duplicado(id_revision),
    id_titulacion_candidata int NOT NULL REFERENCES dbo.titulacion(id_titulacion),
    PRIMARY KEY (id_revision, id_titulacion_candidata)
);

COMMIT;

