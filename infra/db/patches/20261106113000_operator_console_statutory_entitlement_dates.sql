BEGIN;

DO $constraint$
BEGIN
    IF to_regclass('operator_console.statutory_discount_service_channel_reviews') IS NULL THEN
        RAISE EXCEPTION 'operator_console.statutory_discount_service_channel_reviews is required';
    END IF;

    ALTER TABLE operator_console.statutory_discount_service_channel_reviews
        DROP CONSTRAINT IF EXISTS ck_stat_disc_svc_reviews__operator_birth_date;

    ALTER TABLE operator_console.statutory_discount_service_channel_reviews
        DROP CONSTRAINT IF EXISTS ck_stat_disc_svc_reviews__operator_entitlement_date;

    ALTER TABLE operator_console.statutory_discount_service_channel_reviews
        ADD CONSTRAINT ck_stat_disc_svc_reviews__operator_entitlement_date
        CHECK (
            source_channel <> 'OPERATOR_CONSOLE'
            OR (entitlement_type = 'SENIOR_CITIZEN' AND birth_date IS NOT NULL)
            OR (entitlement_type = 'PWD' AND expiry_date IS NOT NULL))
        NOT VALID;

    ALTER TABLE operator_console.statutory_discount_service_channel_reviews
        VALIDATE CONSTRAINT ck_stat_disc_svc_reviews__operator_entitlement_date;
END
$constraint$;

COMMIT;
