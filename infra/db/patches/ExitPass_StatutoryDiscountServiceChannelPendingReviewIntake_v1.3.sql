BEGIN;

DO $$
BEGIN
    IF to_regclass('discounts.statutory_discount_decision_commands') IS NULL THEN
        RAISE EXCEPTION 'missing table discounts.statutory_discount_decision_commands';
    END IF;

    ALTER TABLE discounts.statutory_discount_decision_commands
        DROP CONSTRAINT IF EXISTS ck_statutory_discount_decision_commands__command_status;
    ALTER TABLE discounts.statutory_discount_decision_commands
        ADD CONSTRAINT ck_statutory_discount_decision_commands__command_status
        CHECK (command_status IN (
            'RECEIVED',
            'PROCESSING',
            'AWAITING_REVIEW',
            'COMPLETED',
            'FAILED_RETRYABLE',
            'FAILED_NON_RETRYABLE'
        ));

    ALTER TABLE discounts.statutory_discount_decision_commands
        DROP CONSTRAINT IF EXISTS ck_statutory_discount_decision_commands__result_classification;
    ALTER TABLE discounts.statutory_discount_decision_commands
        ADD CONSTRAINT ck_statutory_discount_decision_commands__result_classification
        CHECK (result_classification IN (
            'ACCEPTED',
            'IDEMPOTENT_REPLAY',
            'AWAITING_REVIEW'
        ));

    ALTER TABLE discounts.statutory_discount_decision_commands
        DROP CONSTRAINT IF EXISTS ck_stat_disc_decision_cmds__recovery;
    ALTER TABLE discounts.statutory_discount_decision_commands
        DROP CONSTRAINT IF EXISTS ck_statutory_discount_decision_commands__recovery_classification;
    ALTER TABLE discounts.statutory_discount_decision_commands
        DROP CONSTRAINT IF EXISTS ck_statutory_discount_decision_commands__recovery_classificatio;
    ALTER TABLE discounts.statutory_discount_decision_commands
        ADD CONSTRAINT ck_stat_disc_decision_cmds__recovery
        CHECK (recovery_classification IN (
            'NONE',
            'AWAITING_REVIEW',
            'READ_CANONICAL_RESULT',
            'RETRY_ORIGINAL_IDEMPOTENCY_KEY',
            'WAIT_THEN_RETRY_ORIGINAL_IDEMPOTENCY_KEY',
            'CORRECT_REQUEST_REQUIRED',
            'NOT_RECOVERABLE'
        ));
END $$;

COMMENT ON CONSTRAINT ck_statutory_discount_decision_commands__command_status
    ON discounts.statutory_discount_decision_commands IS
    'Allows explicit service-channel pending-review decision-v2 intake without overloading PROCESSING.';

COMMENT ON CONSTRAINT ck_stat_disc_decision_cmds__recovery
    ON discounts.statutory_discount_decision_commands IS
    'Allows explicit wait-for-review recovery posture for service-channel pending-review intake.';

COMMENT ON CONSTRAINT ck_statutory_discount_decision_commands__result_classification
    ON discounts.statutory_discount_decision_commands IS
    'Allows pending-review one-shot classification while preserving historical accepted and replay classifications.';

COMMIT;
