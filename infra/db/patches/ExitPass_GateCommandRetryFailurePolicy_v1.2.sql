/*
 * ExitPass v1.2 durable SQL patch.
 *
 * BRD:
 * - 9.13 Timeout, Retry, and Duplicate Handling
 * - 10.7.8 Single-Use Consume Invariant
 *
 * SDD:
 * - Gate Integration Service vendor-neutral gate command retry and failure policy
 *
 * System Invariants:
 * - Gate commands are retried only within a persisted bounded policy.
 * - Terminal gate command failures prevent further adapter invocation.
 * - Retry and failure metadata remains auditable per command.
 */

ALTER TABLE gates.gate_commands
    ADD COLUMN IF NOT EXISTS max_attempts integer,
    ADD COLUMN IF NOT EXISTS retry_policy_code varchar(128),
    ADD COLUMN IF NOT EXISTS last_attempted_at timestamptz,
    ADD COLUMN IF NOT EXISTS next_attempt_at timestamptz,
    ADD COLUMN IF NOT EXISTS terminal_failure_at timestamptz,
    ADD COLUMN IF NOT EXISTS last_failure_code varchar(128),
    ADD COLUMN IF NOT EXISTS last_failure_reason text;

UPDATE gates.gate_commands
SET
    max_attempts = COALESCE(max_attempts, 3),
    retry_policy_code = COALESCE(retry_policy_code, 'GATE_COMMAND_RETRY_V1'),
    last_attempted_at = COALESCE(last_attempted_at, started_at, requested_at),
    last_failure_code = COALESCE(last_failure_code, failure_code),
    last_failure_reason = COALESCE(last_failure_reason, failure_reason),
    terminal_failure_at = CASE
        WHEN command_status = 'TERMINAL_FAILURE'
            THEN COALESCE(terminal_failure_at, completed_at, updated_at)
        ELSE terminal_failure_at
    END,
    next_attempt_at = CASE
        WHEN command_status = 'RETRYABLE'
            THEN COALESCE(next_attempt_at, completed_at, updated_at)
        ELSE next_attempt_at
    END
WHERE max_attempts IS NULL
   OR retry_policy_code IS NULL
   OR last_attempted_at IS NULL
   OR last_failure_code IS DISTINCT FROM failure_code
   OR last_failure_reason IS DISTINCT FROM failure_reason
   OR (command_status = 'TERMINAL_FAILURE' AND terminal_failure_at IS NULL)
   OR (command_status = 'RETRYABLE' AND next_attempt_at IS NULL);

ALTER TABLE gates.gate_commands
    ALTER COLUMN max_attempts SET NOT NULL,
    ALTER COLUMN retry_policy_code SET NOT NULL,
    ALTER COLUMN last_attempted_at SET NOT NULL,
    ALTER COLUMN max_attempts SET DEFAULT 3,
    ALTER COLUMN retry_policy_code SET DEFAULT 'GATE_COMMAND_RETRY_V1';

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint con
        JOIN pg_class cls ON cls.oid = con.conrelid
        JOIN pg_namespace n ON n.oid = cls.relnamespace
        WHERE n.nspname = 'gates'
          AND cls.relname = 'gate_commands'
          AND con.conname = 'ck_gate_commands__max_attempts'
    ) THEN
        ALTER TABLE gates.gate_commands
            ADD CONSTRAINT ck_gate_commands__max_attempts
            CHECK (max_attempts >= 1);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint con
        JOIN pg_class cls ON cls.oid = con.conrelid
        JOIN pg_namespace n ON n.oid = cls.relnamespace
        WHERE n.nspname = 'gates'
          AND cls.relname = 'gate_commands'
          AND con.conname = 'ck_gate_commands__attempt_policy'
    ) THEN
        ALTER TABLE gates.gate_commands
            ADD CONSTRAINT ck_gate_commands__attempt_policy
            CHECK (attempt_count <= max_attempts);
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint con
        JOIN pg_class cls ON cls.oid = con.conrelid
        JOIN pg_namespace n ON n.oid = cls.relnamespace
        WHERE n.nspname = 'gates'
          AND cls.relname = 'gate_commands'
          AND con.conname = 'ck_gate_commands__retryable_next_attempt'
    ) THEN
        ALTER TABLE gates.gate_commands
            ADD CONSTRAINT ck_gate_commands__retryable_next_attempt
            CHECK (
                (command_status = 'RETRYABLE' AND next_attempt_at IS NOT NULL)
                OR (command_status <> 'RETRYABLE' AND next_attempt_at IS NULL)
            );
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint con
        JOIN pg_class cls ON cls.oid = con.conrelid
        JOIN pg_namespace n ON n.oid = cls.relnamespace
        WHERE n.nspname = 'gates'
          AND cls.relname = 'gate_commands'
          AND con.conname = 'ck_gate_commands__terminal_failure_at'
    ) THEN
        ALTER TABLE gates.gate_commands
            ADD CONSTRAINT ck_gate_commands__terminal_failure_at
            CHECK (
                (command_status = 'TERMINAL_FAILURE' AND terminal_failure_at IS NOT NULL)
                OR (command_status <> 'TERMINAL_FAILURE' AND terminal_failure_at IS NULL)
            );
    END IF;
END;
$$;

CREATE INDEX IF NOT EXISTS ix_gate_commands__next_attempt_at
    ON gates.gate_commands (next_attempt_at)
    WHERE command_status = 'RETRYABLE';

CREATE INDEX IF NOT EXISTS ix_gate_commands__terminal_failure_at
    ON gates.gate_commands (terminal_failure_at)
    WHERE command_status = 'TERMINAL_FAILURE';
