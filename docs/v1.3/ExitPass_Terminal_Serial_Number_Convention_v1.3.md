# ExitPass terminal serial-number convention

Terminal serial numbers identify the persistent terminal installation. They are separate from the canonical Terminal ID, Device Administration UUID, POS fiscal terminal UUID, POS Server machine serial number, and BIR MIN.

## Physical APT

- Record the workstation manufacturer's serial number or the approved immutable hardware asset serial when the manufacturer serial is unavailable.
- Enter the value during Device Administration registration and enrollment.
- A replacement workstation must have a different serial number.
- Do not derive the serial from the cashier, shift, deployment, or application startup.

## Logical WebPay terminal

- Assign an administrative logical serial using `EP-{SITE}-WEB-{NNNN}`.
- The PITX assignment is `EP-PITX-L3-WEB-0001`.
- Store the value on the existing POS `channel_terminals` registration.
- Do not derive the serial from a browser or customer session.

Serial values are unique among active terminals for a Site POS Server. New Sales Invoices copy the registered value into the immutable header snapshot; historical fiscal documents are not backfilled.
