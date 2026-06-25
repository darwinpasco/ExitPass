# ExitPass POS Server System Design Diagram Index

Version: v1.0 planning artifact
Status: Draft for system design planning only
Generated: 2026-06-25

## Diagram Index

| Diagram ID | Title | Purpose | JPEG path | PlantUML source path | Future System Design section |
| --- | --- | --- | --- | --- | --- |
| PSD-D01 | POS Server Context and Authority Boundary | Shows POS Server relationships and the Central PMS/POS authority boundary. | `diagrams/ExitPass_POS_Server_Context_Authority_Boundary.jpg` | `diagrams/ExitPass_POS_Server_Context_Authority_Boundary.puml` | 5. POS Server Context; 7. Authority Model |
| PSD-D02 | POS Server Component Architecture | Shows logical POS Server components and their fiscal responsibilities. | `diagrams/ExitPass_POS_Server_Component_Architecture.jpg` | `diagrams/ExitPass_POS_Server_Component_Architecture.puml` | 8. Component Architecture |
| PSD-D03 | Payment Finality to SI to ExitAuthorization Sequence | Shows the required sequence from verified payment finality through SI issuance to ExitAuthorization. | `diagrams/ExitPass_POS_Server_Payment_Finality_to_SI_to_ExitAuthorization.jpg` | `diagrams/ExitPass_POS_Server_Payment_Finality_to_SI_to_ExitAuthorization.puml` | 11. Sales Invoice Lifecycle; 31. Integration With Central PMS |
| PSD-D04 | Digital SI URL and QR Code Presentation Model | Shows digital SI URL generation and QR presentation as a channel/terminal capability. | `diagrams/ExitPass_Digital_SI_URL_QR_Presentation_Model.jpg` | `diagrams/ExitPass_Digital_SI_URL_QR_Presentation_Model.puml` | 12. Printed and Digital Sales Invoice Delivery; 13. Digital SI URL and QR Code Model |
| PSD-D05 | Fiscal Output and Reporting Pipeline | Shows canonical fiscal records feeding print, digital SI, EJ, POSLog, reports, exports, audit, reprints, and adjustments. | `diagrams/ExitPass_POS_Server_Fiscal_Output_Reporting_Pipeline.jpg` | `diagrams/ExitPass_POS_Server_Fiscal_Output_Reporting_Pipeline.puml` | 19-25. Reporting, logs, exports, audit, reprints, adjustments |
| PSD-D06 | Fiscal Counters and Recovery Continuity Model | Shows SI sequence, adjustment sequence, counters, GTA, EJ hash, last event timestamp, restore/failover, supervised recovery, and recovery audit. | `diagrams/ExitPass_POS_Server_Counters_Recovery_Continuity_Model.jpg` | `diagrams/ExitPass_POS_Server_Counters_Recovery_Continuity_Model.puml` | 18. Counters; 28-29. Integrity and Recovery |
| PSD-D07 | Fiscal Issuance Failure and Retry Flow | Shows fiscal issuance failure, retry, blocked authorization, messaging, supervisor-approved exception, incident/reconciliation tagging, and controlled closure. | `diagrams/ExitPass_POS_Server_Fiscal_Issuance_Failure_Retry_Flow.jpg` | `diagrams/ExitPass_POS_Server_Fiscal_Issuance_Failure_Retry_Flow.puml` | 30. Exception and Retry Handling |

## Diagram Maintenance Notes

- PlantUML files are the editable sources.
- JPEG files are generated outputs for document consumption.
- Diagrams are planning artifacts and may be refined when the full System Design is drafted.
- Diagrams must continue to preserve the approved authority model: Central PMS owns payment finality and ExitAuthorization; POS Server owns fiscal issuance and reporting.

