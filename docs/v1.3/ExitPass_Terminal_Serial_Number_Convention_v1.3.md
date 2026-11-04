# ExitPass terminal serial-number convention

Terminal serial numbers identify the persistent terminal installation. They are separate from the canonical Terminal ID, Device Administration UUID, POS fiscal terminal UUID, POS Server machine serial number, and BIR MIN.

## Physical APT

- Record the workstation manufacturer's serial number or the approved immutable hardware asset serial when the manufacturer serial is unavailable.
- Enter the value during Device Administration registration and enrollment.
- A replacement workstation must have a different serial number.
- Do not derive the serial from the cashier, shift, deployment, or application startup.

## Logical WebPay terminal

- Use the approved registered terminal serial assigned to the logical WebPay installation.
- The PITX Level 3 Terminal ID is `PITX-WEBPAY-01` and its approved serial is `0026-B738-43B5-E605`.
- Store the value on the existing POS `channel_terminals` registration.
- Do not derive the serial from a browser or customer session.

## PITX Level 3 registered identities

- Site POS Server code: `PITX-L3-POS-01`
- POS Server serial number: `0026_B728_3B2F_8505`
- APT Terminal ID: `PITX-APT-01`
- APT terminal serial number: `ACE4_2E00_653A_D952`
- WebPay Terminal ID: `PITX-WEBPAY-01`
- WebPay terminal serial number: `0026-B738-43B5-E605`

Serial values are unique among active terminals for a Site POS Server. New Sales Invoices copy the registered value into the immutable header snapshot; historical fiscal documents are not backfilled.
