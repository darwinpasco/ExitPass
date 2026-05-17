# ExitPass WebPay Complete Asset Pack

Copy the `public/assets` folder into:

```text
D:\SourceCodes\ExitPass\src\Services\WebPayUi\public\assets
```

## Folder structure

```text
public/assets/
  logo/
    exitpass-logo.svg
    favicon.svg
    proparking-logo.png
    proparking-favicon.svg

  payment-methods/
    gcash.png
    maya.png
    qrph.png
    visa.png
    mastercard.png
    cards-visa-mastercard.png

  icons/
    qr-scan.svg
    ticket.svg
    plate-number.svg
    payment.svg
    success.svg
    error.svg
    back.svg

  illustrations/
    camera-permission.svg
    payment-pending.svg
    payment-success.svg
    payment-failed.svg
    empty-state.svg

  brand/
    colors.json
    theme.ts
    asset-manifest.json
```

## UI reference paths

```text
/assets/logo/exitpass-logo.svg
/assets/logo/favicon.svg
/assets/logo/proparking-logo.png
/assets/logo/proparking-favicon.svg

/assets/payment-methods/gcash.png
/assets/payment-methods/maya.png
/assets/payment-methods/qrph.png
/assets/payment-methods/visa.png
/assets/payment-methods/mastercard.png
/assets/payment-methods/cards-visa-mastercard.png

/assets/icons/qr-scan.svg
/assets/icons/ticket.svg
/assets/icons/plate-number.svg
/assets/icons/payment.svg
/assets/icons/success.svg
/assets/icons/error.svg
/assets/icons/back.svg

/assets/illustrations/camera-permission.svg
/assets/illustrations/payment-pending.svg
/assets/illustrations/payment-success.svg
/assets/illustrations/payment-failed.svg
/assets/illustrations/empty-state.svg
```

## Notes

- Payment logos were cleaned and resized from the images you provided.
- SVG UI icons and illustrations are lightweight and safe for the WebPay baseline.
- For production, replace raster payment logos with official brand-owner vector/source files when available.
- Missing generated raster files: []
