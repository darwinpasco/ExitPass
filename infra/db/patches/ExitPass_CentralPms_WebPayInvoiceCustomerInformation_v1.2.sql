-- ExitPass v1.2 additive persistence for immutable WebPay Sales Invoice customer information.
-- This data is fiscal presentation metadata only and cannot alter payable amount or authority state.
CREATE TABLE IF NOT EXISTS core.payment_attempt_invoice_customer_information (
    payment_attempt_id uuid NOT NULL,
    parking_session_id uuid NOT NULL,
    tariff_snapshot_id uuid NOT NULL,
    customer_name varchar(160),
    customer_address varchar(300),
    customer_tin varchar(40),
    business_style varchar(160),
    statutory_id_number varchar(80),
    created_at timestamptz DEFAULT now() NOT NULL,
    CONSTRAINT pk_payment_attempt_invoice_customer_information PRIMARY KEY (payment_attempt_id),
    CONSTRAINT fk_payment_attempt_invoice_customer_information__attempt
        FOREIGN KEY (payment_attempt_id) REFERENCES core.payment_attempts(payment_attempt_id),
    CONSTRAINT fk_payment_attempt_invoice_customer_information__session
        FOREIGN KEY (parking_session_id) REFERENCES core.parking_sessions(parking_session_id),
    CONSTRAINT fk_payment_attempt_invoice_customer_information__tariff
        FOREIGN KEY (tariff_snapshot_id) REFERENCES core.tariff_snapshots(tariff_snapshot_id)
);

COMMENT ON TABLE core.payment_attempt_invoice_customer_information IS
    'Immutable customer-entered Sales Invoice presentation fields bound to a canonical payment attempt.';
