DO $$
DECLARE
    v_index_definition text;
BEGIN
    IF to_regclass('discounts.statutory_discount_payable_basis_recalculations') IS NULL THEN
        RAISE EXCEPTION 'Missing statutory payable-basis recalculation evidence table';
    END IF;

    IF to_regclass('discounts.ux_sd_pbr__previous_applied_tariff') IS NULL
       OR to_regclass('discounts.ux_sd_pbr__original_tariff') IS NULL
       OR to_regclass('discounts.ux_sd_pbr__applied_tariff') IS NULL THEN
        RAISE EXCEPTION 'Missing statutory payable-basis recalculation uniqueness indexes';
    END IF;

    IF NOT EXISTS (
        SELECT 1
        FROM pg_trigger
        WHERE tgrelid = 'discounts.statutory_discount_payable_basis_recalculations'::regclass
          AND tgname = 'trg_sd_pbr__immutable'
          AND NOT tgisinternal
    ) THEN
        RAISE EXCEPTION 'Missing immutable recalculation evidence trigger';
    END IF;

    SELECT pg_get_indexdef(indexrelid)
    INTO v_index_definition
    FROM pg_index
    WHERE indexrelid = to_regclass('core.ux_tariff_snapshots__statutory_discount_validation_applied');

    IF v_index_definition IS NULL THEN
        RAISE EXCEPTION 'Missing statutory adjusted tariff uniqueness index';
    END IF;

    IF position('UNIQUE INDEX' IN upper(v_index_definition)) = 0
       OR position('statutory_discount_validation_id' IN v_index_definition) = 0
       OR position('snapshot_status' IN v_index_definition) = 0
       OR position('ACTIVE' IN v_index_definition) = 0 THEN
        RAISE EXCEPTION 'Unexpected statutory adjusted tariff uniqueness index: %', v_index_definition;
    END IF;

    IF EXISTS (
        SELECT 1
        FROM core.tariff_snapshots
        WHERE statutory_discount_validation_id IS NOT NULL
          AND snapshot_status = 'ACTIVE'::core.tariff_snapshot_status_enum
        GROUP BY statutory_discount_validation_id
        HAVING COUNT(*) > 1
    ) THEN
        RAISE EXCEPTION 'More than one ACTIVE statutory-adjusted tariff exists for a validation';
    END IF;
END $$;
