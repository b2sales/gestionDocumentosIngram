USE master;
GO

IF DB_ID(N'GestionDocumentosLocal') IS NULL
BEGIN
    CREATE DATABASE GestionDocumentosLocal;
END
GO

USE GestionDocumentosLocal;
GO

SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'dbo.GreInfos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.GreInfos
    (
        id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        greName NVARCHAR(128) NOT NULL,
        ordenCompra NVARCHAR(128) NOT NULL CONSTRAINT DF_GreInfos_ordenCompra DEFAULT (N''),
        notaVenta NVARCHAR(128) NOT NULL CONSTRAINT DF_GreInfos_notaVenta DEFAULT (N''),
        delivery NVARCHAR(64) NOT NULL CONSTRAINT DF_GreInfos_delivery DEFAULT (N''),
        facturaSAP NVARCHAR(128) NULL,
        facturaSUNAT NVARCHAR(128) NULL,
        rucTranspor NVARCHAR(32) NOT NULL CONSTRAINT DF_GreInfos_rucTranspor DEFAULT (N''),
        razoTrans NVARCHAR(256) NOT NULL CONSTRAINT DF_GreInfos_razoTrans DEFAULT (N''),
        motivoTraslado INT NOT NULL CONSTRAINT DF_GreInfos_motivoTraslado DEFAULT (0),
        fechaInicioTraslado DATETIME2 NOT NULL CONSTRAINT DF_GreInfos_fechaInicioTraslado DEFAULT (SYSUTCDATETIME()),
        Auditoria_CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_GreInfos_Auditoria_CreatedAt DEFAULT (SYSUTCDATETIME()),
        Auditoria_UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_GreInfos_Auditoria_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        Auditoria_DeletedAt DATETIME2 NULL,
        Auditoria_Deleted BIT NOT NULL CONSTRAINT DF_GreInfos_Auditoria_Deleted DEFAULT (0),
        statusBee NVARCHAR(128) NULL,
        substatusBee NVARCHAR(128) NULL,
        delivered_in_clientBee BIT NULL,
        statusDate DATETIME2 NULL,
        estimatedDeliveryDate DATETIME2 NULL,
        actualDeliveryDate DATETIME2 NULL,
        shipmentStatus NVARCHAR(128) NULL,
        city INT NOT NULL CONSTRAINT DF_GreInfos_city DEFAULT (0),
        stateCode NVARCHAR(32) NULL,
        destinationPostCode NVARCHAR(32) NOT NULL CONSTRAINT DF_GreInfos_destinationPostCode DEFAULT (N''),
        StatusUrbano NVARCHAR(128) NULL,
        SubstatusUrbano NVARCHAR(128) NULL
    );
END
GO

IF OBJECT_ID(N'dbo.Documentos', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Documentos
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        NameFile NVARCHAR(512) NOT NULL,
        TipDoc NVARCHAR(16) NOT NULL,
        Serie NVARCHAR(32) NOT NULL,
        Numero NVARCHAR(32) NOT NULL,
        Fecha NVARCHAR(32) NOT NULL,
        Cod_SAP NVARCHAR(64) NOT NULL,
        CodVen NVARCHAR(64) NOT NULL,
        Ruc NVARCHAR(32) NOT NULL,
        Cliente NVARCHAR(512) NOT NULL,
        Moneda NVARCHAR(16) NOT NULL,
        NumPed NVARCHAR(64) NOT NULL,
        FacInterno NVARCHAR(64) NOT NULL,
        Monto NVARCHAR(64) NOT NULL,
        FecVenci NVARCHAR(32) NOT NULL,
        ConPag NVARCHAR(64) NOT NULL,
        Contacto NVARCHAR(256) NOT NULL,
        Sunat NVARCHAR(64) NOT NULL,
        Estado NVARCHAR(64) NOT NULL,
        EstCobranza NVARCHAR(64) NOT NULL,
        Deli NVARCHAR(64) NOT NULL,
        Situacion NVARCHAR(64) NOT NULL,
        indNotificacion INT NOT NULL CONSTRAINT DF_Documentos_indNotificacion DEFAULT (0),
        paymentIssueTime NVARCHAR(64) NOT NULL CONSTRAINT DF_Documentos_paymentIssueTime DEFAULT (N''),
        referenceDocNumber NVARCHAR(128) NOT NULL CONSTRAINT DF_Documentos_referenceDocNumber DEFAULT (N''),
        orderReason NVARCHAR(256) NOT NULL CONSTRAINT DF_Documentos_orderReason DEFAULT (N''),
        MontoDetraccion DECIMAL(18,2) NOT NULL CONSTRAINT DF_Documentos_MontoDetraccion DEFAULT (0),
        PorcentajeDet DECIMAL(18,2) NOT NULL CONSTRAINT DF_Documentos_PorcentajeDet DEFAULT (0),
        customerOrderNumber NVARCHAR(128) NOT NULL CONSTRAINT DF_Documentos_customerOrderNumber DEFAULT (N'')
    );
END
GO

IF OBJECT_ID(N'dbo.DET_DOCUMENTOS', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.DET_DOCUMENTOS
    (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        FacInterno NVARCHAR(64) NOT NULL,
        NumPed NVARCHAR(64) NOT NULL,
        Cod_Material NVARCHAR(64) NOT NULL,
        Descripcion NVARCHAR(512) NOT NULL,
        Cantidad NVARCHAR(64) NOT NULL
    );
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Documentos_NameFile' AND object_id = OBJECT_ID(N'dbo.Documentos'))
BEGIN
    CREATE UNIQUE INDEX UX_Documentos_NameFile ON dbo.Documentos(NameFile);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_GreInfos_greName' AND object_id = OBJECT_ID(N'dbo.GreInfos'))
BEGIN
    CREATE UNIQUE INDEX UX_GreInfos_greName
        ON dbo.GreInfos(greName)
        WHERE Auditoria_Deleted = 0;
END
GO
