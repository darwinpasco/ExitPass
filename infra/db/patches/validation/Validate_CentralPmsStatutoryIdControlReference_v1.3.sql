DO $validation$
DECLARE
    column_count integer;
    masked_column_count integer;
    constraint_count integer;
    invalid_value_count integer;
BEGIN
    SELECT count(*)
      INTO column_count
      FROM information_schema.columns
     WHERE (table_schema, table_name) IN (
               ('operator_console', 'statutory_discount_service_channel_reviews'),
               ('discounts', 'statutory_discount_validations'))
       AND column_name = 'id_control_reference'
       AND data_type = 'character varying'
       AND character_maximum_length = 64
       AND is_nullable = 'YES'
       AND column_default IS NULL;

    SELECT count(*)
      INTO masked_column_count
      FROM information_schema.columns
     WHERE (table_schema, table_name) IN (
               ('operator_console', 'statutory_discount_service_channel_reviews'),
               ('discounts', 'statutory_discount_validations'))
       AND column_name = 'masked_id_reference';

    SELECT count(*)
      INTO constraint_count
      FROM pg_constraint
     WHERE (
           conrelid = 'discounts.statutory_discount_validations'::regclass
           AND conname = 'ck_stat_disc_validations__id_control_reference_valid'
           AND contype = 'c'
           AND convalidated
       )
       OR (
           conrelid = 'operator_console.statutory_discount_service_channel_reviews'::regclass
           AND conname = 'ck_stat_disc_svc_reviews__id_control_reference_valid'
           AND contype = 'c'
           AND convalidated
       );

    SELECT sum(invalid_count)
      INTO invalid_value_count
      FROM (
          SELECT count(*) AS invalid_count
          FROM operator_console.statutory_discount_service_channel_reviews
          WHERE id_control_reference IS NOT NULL
            AND (
                char_length(id_control_reference) NOT BETWEEN 4 AND 64
                OR id_control_reference <> btrim(id_control_reference)
                OR id_control_reference ~ '[[:space:]]'
                OR position('*' in id_control_reference) > 0)
          UNION ALL
          SELECT count(*) AS invalid_count
          FROM discounts.statutory_discount_validations
          WHERE id_control_reference IS NOT NULL
            AND (
                char_length(id_control_reference) NOT BETWEEN 4 AND 64
                OR id_control_reference <> btrim(id_control_reference)
                OR id_control_reference ~ '[[:space:]]'
                OR position('*' in id_control_reference) > 0)
      ) AS invalid_values;

    IF column_count <> 2
       OR masked_column_count <> 2
       OR constraint_count <> 2
       OR invalid_value_count <> 0 THEN
        RAISE EXCEPTION 'CENTRAL_PMS_STATUTORY_ID_CONTROL_REFERENCE_INVALID';
    END IF;

    RAISE NOTICE 'CENTRAL_PMS_STATUTORY_ID_CONTROL_REFERENCE_VALID';
END
$validation$;
