IF OBJECT_ID(N'[Callback].[Client]', N'U') IS NULL
BEGIN
    CREATE TABLE [Callback].[Client]
    (
        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_Callback_Client] PRIMARY KEY,
        [Name] nvarchar(200) NOT NULL,
        [CreatedAt] datetimeoffset(7) NOT NULL,
        [UpdatedAt] datetimeoffset(7) NOT NULL,
        CONSTRAINT [UQ_Callback_Client_Name] UNIQUE ([Name])
    );
END;

IF OBJECT_ID(N'[Callback].[Endpoint]', N'U') IS NULL
BEGIN
    CREATE TABLE [Callback].[Endpoint]
    (
        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_Callback_Endpoint] PRIMARY KEY,
        [ClientId] uniqueidentifier NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Url] nvarchar(2048) NOT NULL,
        [HttpMethod] varchar(10) NOT NULL,
        [ConfigurationHash] char(64) NOT NULL,
        [CreatedAt] datetimeoffset(7) NOT NULL,
        [UpdatedAt] datetimeoffset(7) NOT NULL,
        CONSTRAINT [FK_Callback_Endpoint_Client] FOREIGN KEY ([ClientId])
            REFERENCES [Callback].[Client] ([Id]),
        CONSTRAINT [UQ_Callback_Endpoint_Client_Name] UNIQUE ([ClientId], [Name])
    );
END;

IF OBJECT_ID(N'[Callback].[Job]', N'U') IS NULL
BEGIN
    CREATE TABLE [Callback].[Job]
    (
        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_Callback_Job] PRIMARY KEY,
        [ClientId] uniqueidentifier NOT NULL,
        [EndpointId] uniqueidentifier NOT NULL,
        [EventName] nvarchar(200) NOT NULL,
        [Payload] nvarchar(max) NOT NULL,
        [ContentType] nvarchar(100) NOT NULL,
        [EndpointSnapshot] nvarchar(max) NOT NULL,
        [Status] tinyint NOT NULL,
        [CreatedAt] datetimeoffset(7) NOT NULL,
        [NextAttemptAt] datetimeoffset(7) NOT NULL,
        [CompletedAt] datetimeoffset(7) NULL,
        [AttemptCount] int NOT NULL CONSTRAINT [DF_Callback_Job_AttemptCount] DEFAULT (0),
        [MaxAttempts] int NOT NULL,
        [CorrelationId] nvarchar(200) NULL,
        [IdempotencyKey] nvarchar(200) NOT NULL,
        [LockedBy] nvarchar(200) NULL,
        [LockedUntil] datetimeoffset(7) NULL,
        [LastError] nvarchar(2048) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [FK_Callback_Job_Client] FOREIGN KEY ([ClientId])
            REFERENCES [Callback].[Client] ([Id]),
        CONSTRAINT [FK_Callback_Job_Endpoint] FOREIGN KEY ([EndpointId])
            REFERENCES [Callback].[Endpoint] ([Id]),
        CONSTRAINT [CK_Callback_Job_Status] CHECK ([Status] BETWEEN 0 AND 3),
        CONSTRAINT [CK_Callback_Job_MaxAttempts] CHECK ([MaxAttempts] > 0),
        CONSTRAINT [CK_Callback_Job_EndpointSnapshotJson] CHECK (ISJSON([EndpointSnapshot]) = 1)
    );

    CREATE INDEX [IX_Callback_Job_Claim]
        ON [Callback].[Job] ([Status], [NextAttemptAt], [LockedUntil], [CreatedAt])
        INCLUDE ([AttemptCount], [MaxAttempts]);

    CREATE INDEX [IX_Callback_Job_Client_CreatedAt]
        ON [Callback].[Job] ([ClientId], [CreatedAt] DESC);

    CREATE INDEX [IX_Callback_Job_ExpiredLease]
        ON [Callback].[Job] ([Status], [LockedUntil])
        INCLUDE ([AttemptCount], [MaxAttempts]);
END;

IF OBJECT_ID(N'[Callback].[Attempt]', N'U') IS NULL
BEGIN
    CREATE TABLE [Callback].[Attempt]
    (
        [Id] uniqueidentifier NOT NULL CONSTRAINT [PK_Callback_Attempt] PRIMARY KEY,
        [JobId] uniqueidentifier NOT NULL,
        [AttemptNumber] int NOT NULL,
        [StartedAt] datetimeoffset(7) NOT NULL,
        [CompletedAt] datetimeoffset(7) NULL,
        [DurationMs] bigint NULL,
        [HttpStatusCode] int NULL,
        [FailureType] tinyint NULL,
        [ErrorMessage] nvarchar(2048) NULL,
        [ResponseBody] nvarchar(max) NULL,
        [WorkerId] nvarchar(200) NOT NULL,
        CONSTRAINT [FK_Callback_Attempt_Job] FOREIGN KEY ([JobId])
            REFERENCES [Callback].[Job] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [UQ_Callback_Attempt_Job_Number] UNIQUE ([JobId], [AttemptNumber])
    );

    CREATE INDEX [IX_Callback_Attempt_Job_StartedAt]
        ON [Callback].[Attempt] ([JobId], [StartedAt] DESC);
END;
