DO $$
DECLARE
    actual_permissions text[];
    expected_permissions constant text[] := ARRAY[
        'statutory-discounts.decision.approve',
        'statutory-discounts.decision.reject',
        'statutory-discounts.evidence.review.view',
        'statutory-discounts.review.detail.read',
        'statutory-discounts.review.queue.read'
    ];
BEGIN
    IF (SELECT count(*) FROM identity.roles WHERE role_code = 'STATUTORY_DISCOUNT_PROCESSOR') <> 1 THEN
        RAISE EXCEPTION 'Expected exactly one Statutory Discount Processor role.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM identity.roles
        WHERE role_code = 'STATUTORY_DISCOUNT_PROCESSOR'
          AND (role_name <> 'Statutory Discount Processor'
               OR role_status <> 'ACTIVE'
               OR role_provenance <> 'CANONICAL_ROLE'
               OR NOT human_assignable
               OR NOT direct_add_user_eligible
               OR requires_elevated_approval)
    ) THEN
        RAISE EXCEPTION 'Statutory Discount Processor canonical role attributes are invalid.';
    END IF;

    SELECT array_agg(p.permission_code ORDER BY p.permission_code)
    INTO actual_permissions
    FROM identity.role_permissions rp
    JOIN identity.roles r ON r.role_id = rp.role_id
    JOIN identity.permissions p ON p.permission_id = rp.permission_id
    WHERE r.role_code = 'STATUTORY_DISCOUNT_PROCESSOR'
      AND rp.binding_status = 'ACTIVE'
      AND rp.effective_from <= now()
      AND (rp.effective_to IS NULL OR rp.effective_to > now())
      AND rp.revoked_at IS NULL;

    IF actual_permissions IS DISTINCT FROM expected_permissions THEN
        RAISE EXCEPTION 'Statutory Discount Processor permission bundle mismatch. Expected %, found %.',
            expected_permissions, actual_permissions;
    END IF;

    IF actual_permissions @> ARRAY['statutory-discounts.payable-basis.apply']::text[] THEN
        RAISE EXCEPTION 'Statutory Discount Processor must not apply payable bases.';
    END IF;

    RAISE NOTICE 'IDENTITY_RBAC_STATUTORY_DISCOUNT_PROCESSOR_VALID';
END
$$;
