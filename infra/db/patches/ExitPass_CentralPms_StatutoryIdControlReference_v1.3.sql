BEGIN;

ALTER TABLE operator_console.statutory_discount_service_channel_reviews
    ADD COLUMN IF NOT EXISTS id_control_reference varchar(64);

ALTER TABLE discounts.statutory_discount_validations
    ADD COLUMN IF NOT EXISTS id_control_reference varchar(64);

COMMENT ON COLUMN operator_console.statutory_discount_service_channel_reviews.id_control_reference IS
    'Complete normalized statutory ID/control reference submitted by the customer and correctable by an authorized reviewer. Public presentation must use masked_id_reference.';

COMMENT ON COLUMN discounts.statutory_discount_validations.id_control_reference IS
    'Complete normalized statutory ID/control reference finalized by an authorized reviewer. Presentation masking is applied outside persistence.';

DO $patch$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'operator_console.statutory_discount_service_channel_reviews'::regclass
          AND conname = 'ck_stat_disc_svc_reviews__id_control_reference_valid'
    ) THEN
        ALTER TABLE operator_console.statutory_discount_service_channel_reviews
            ADD CONSTRAINT ck_stat_disc_svc_reviews__id_control_reference_valid
            CHECK (
                id_control_reference IS NULL
                OR (
                    char_length(id_control_reference) BETWEEN 4 AND 64
                    AND id_control_reference = btrim(id_control_reference)
                    AND id_control_reference !~ '[[:space:]]'
                    AND position('*' in id_control_reference) = 0
                )
            );
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conrelid = 'discounts.statutory_discount_validations'::regclass
          AND conname = 'ck_stat_disc_validations__id_control_reference_valid'
    ) THEN
        ALTER TABLE discounts.statutory_discount_validations
            ADD CONSTRAINT ck_stat_disc_validations__id_control_reference_valid
            CHECK (
                id_control_reference IS NULL
                OR (
                    char_length(id_control_reference) BETWEEN 4 AND 64
                    AND id_control_reference = btrim(id_control_reference)
                    AND id_control_reference !~ '[[:space:]]'
                    AND position('*' in id_control_reference) = 0
                )
            );
    END IF;
END
$patch$;

COMMIT;
