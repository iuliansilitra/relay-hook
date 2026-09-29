IF COL_LENGTH(N'[Callback].[Job]', N'CompletedAttemptCount') IS NULL
BEGIN
    ALTER TABLE [Callback].[Job]
        ADD [CompletedAttemptCount] int NULL;

    EXEC(N'
        UPDATE [Job]
        SET [CompletedAttemptCount] =
        (
            SELECT COUNT_BIG(*)
            FROM [Callback].[Attempt] AS [Attempt]
            WHERE [Attempt].[JobId] = [Job].[Id]
              AND [Attempt].[CompletedAt] IS NOT NULL
        )
        FROM [Callback].[Job] AS [Job];

        ALTER TABLE [Callback].[Job]
            ALTER COLUMN [CompletedAttemptCount] int NOT NULL;

        ALTER TABLE [Callback].[Job]
            ADD CONSTRAINT [DF_Callback_Job_CompletedAttemptCount]
            DEFAULT (0) FOR [CompletedAttemptCount];

        ALTER TABLE [Callback].[Job]
            ADD CONSTRAINT [CK_Callback_Job_AttemptCounts]
            CHECK ([AttemptCount] >= 0
               AND [CompletedAttemptCount] >= 0
               AND [CompletedAttemptCount] <= [AttemptCount]);
    ');
END;

CREATE INDEX [IX_Callback_Job_Claim]
    ON [Callback].[Job] ([Status], [NextAttemptAt], [LockedUntil], [CreatedAt])
    INCLUDE ([AttemptCount], [CompletedAttemptCount], [MaxAttempts])
    WITH (DROP_EXISTING = ON);

CREATE INDEX [IX_Callback_Job_ExpiredLease]
    ON [Callback].[Job] ([Status], [LockedUntil])
    INCLUDE ([AttemptCount], [CompletedAttemptCount], [MaxAttempts])
    WITH (DROP_EXISTING = ON);

IF NOT EXISTS
(
    SELECT 1 FROM sys.indexes
    WHERE [object_id] = OBJECT_ID(N'[Callback].[Job]')
      AND [name] = N'IX_Callback_Job_Retention'
)
BEGIN
    CREATE INDEX [IX_Callback_Job_Retention]
        ON [Callback].[Job] ([Status], [CompletedAt]);
END;
