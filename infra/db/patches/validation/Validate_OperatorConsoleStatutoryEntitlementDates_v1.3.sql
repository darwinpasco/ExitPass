DO $validation$
DECLARE
    definition text;
BEGIN
    SELECT pg_get_constraintdef(oid)
      INTO definition
      FROM pg_constraint
     WHERE conrelid = 'operator_console.statutory_discount_service_channel_reviews'::regclass
       AND conname = 'ck_stat_disc_svc_reviews__operator_entitlement_date'
       AND contype = 'c'
       AND convalidated;

    IF definition IS NULL
       OR definition NOT LIKE '%SENIOR_CITIZEN%birth_date IS NOT NULL%'
       OR definition NOT LIKE '%PWD%expiry_date IS NOT NULL%' THEN
        RAISE EXCEPTION 'Operator Console statutory entitlement-date constraint is missing or invalid';
    END IF;

    IF EXISTS (
        SELECT 1
          FROM pg_constraint
         WHERE conrelid = 'operator_console.statutory_discount_service_channel_reviews'::regclass
           AND conname = 'ck_stat_disc_svc_reviews__operator_birth_date') THEN
        RAISE EXCEPTION 'Retired unconditional Operator Console birth-date constraint remains installed';
    END IF;
END
$validation$;
