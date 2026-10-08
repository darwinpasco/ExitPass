/*
 * ExitPass v1.3 additive Operator Console shift-control deferment.
 *
 * Existing shift history and the foreign key remain intact. New server-owned
 * operating contexts no longer require a shift reference.
 */

ALTER TABLE operator_console.operator_session_contexts
    ALTER COLUMN operator_shift_id DROP NOT NULL;

COMMENT ON TABLE operator_console.operator_session_contexts IS
    'Server-owned H-006 Operator Console device and effective Site/Site Group binding; an optional shift reference is retained only for historical compatibility.';

COMMENT ON COLUMN operator_console.operator_session_contexts.operator_shift_id IS
    'Optional historical Operator Console shift reference; not part of the v1.3 authorization boundary.';
