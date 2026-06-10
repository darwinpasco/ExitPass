using System.Globalization;
using System.Text;

namespace ExitPass.CentralPms.Application.OperatorConsole;

public sealed class OperatorConsoleProductionPolicyImportService : IOperatorConsoleProductionPolicyImportService
{
    private static readonly string[] ImportColumns =
    [
        "policy_code",
        "policy_name",
        "entitlement_type",
        "lgu_code",
        "jurisdiction_name",
        "site_group_code",
        "site_code",
        "policy_level",
        "policy_type",
        "policy_resolution_basis",
        "benefit_type",
        "discount_base_scope",
        "free_duration_minutes",
        "initial_rate_exempt",
        "full_fee_exempt",
        "overnight_excluded",
        "valet_excluded",
        "standalone_parking_excluded",
        "driver_or_passenger_required",
        "beneficiary_residency_scope",
        "requires_evidence",
        "required_evidence_type",
        "requires_operator_validation",
        "legal_basis_reference",
        "ordinance_reference",
        "national_law_reference",
        "source_reference",
        "verification_status",
        "effective_from",
        "effective_to",
        "reviewed_by",
        "reviewed_at",
        "approved_by",
        "approved_at",
        "notes"
    ];

    private static readonly string[] ReviewColumns =
    [
        "review_status",
        "review_owner",
        "legal_review_decision",
        "product_review_decision",
        "ops_review_decision",
        "engineering_review_decision",
        "qa_review_decision",
        "approval_notes"
    ];

    private static readonly string[] RequiredColumns =
    [
        "policy_code",
        "policy_name",
        "entitlement_type",
        "policy_level",
        "policy_type",
        "policy_resolution_basis",
        "benefit_type",
        "discount_base_scope",
        "initial_rate_exempt",
        "full_fee_exempt",
        "overnight_excluded",
        "valet_excluded",
        "standalone_parking_excluded",
        "driver_or_passenger_required",
        "beneficiary_residency_scope",
        "requires_evidence",
        "requires_operator_validation",
        "source_reference",
        "verification_status",
        "effective_from"
    ];

    private static readonly string[] BooleanColumns =
    [
        "initial_rate_exempt",
        "full_fee_exempt",
        "overnight_excluded",
        "valet_excluded",
        "standalone_parking_excluded",
        "driver_or_passenger_required",
        "requires_evidence",
        "requires_operator_validation"
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedValues =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["entitlement_type"] = Set("SENIOR_CITIZEN", "PWD"),
            ["policy_level"] = Set("NATIONAL_LAW", "LOCAL_ORDINANCE", "SITE_POLICY", "OPERATIONAL_POLICY"),
            ["policy_type"] = Set("LEGAL_REFERENCE", "LOCAL_ORDINANCE", "SITE_POLICY", "OPERATIONAL_POLICY", "IMPLEMENTATION_POLICY"),
            ["policy_resolution_basis"] = Set("LOCAL_ORDINANCE_APPLIED", "NATIONAL_LAW_FALLBACK", "SITE_POLICY_OPERATIONAL_ONLY", "MANUAL_POLICY_SELECTION", "SYSTEM_DEFAULT"),
            ["benefit_type"] = Set("STATUTORY_DISCOUNT_VAT_EXEMPT", "FREE_DURATION", "INITIAL_RATE_EXEMPTION", "FULL_FEE_EXEMPTION", "LOCAL_RULE", "MANUAL_REVIEW"),
            ["discount_base_scope"] = Set("VAT_EXCLUSIVE", "GROSS", "NET", "NOT_APPLICABLE"),
            ["beneficiary_residency_scope"] = Set("RESIDENT_ONLY", "NON_RESIDENT_ALLOWED", "MIXED_OR_CONFLICTING", "UNVERIFIED", "NOT_APPLICABLE"),
            ["required_evidence_type"] = Set("SENIOR_CITIZEN_ID", "PWD_ID", "AUTHORIZATION_LETTER", "SUPPORTING_DOCUMENT", "VALIDATION_SCREENSHOT", "HASH_ONLY_REFERENCE", "OTHER"),
            ["verification_status"] = Set("LEAD_UNVERIFIED", "VERIFIED_SECONDARY", "VERIFIED_OFFICIAL", "APPROVED_FOR_PILOT", "ACTIVE_APPROVED", "PROPOSED_ONLY", "REJECTED"),
            ["review_status"] = Set("APPROVE_FOR_IMPORT", "APPROVE_FOR_PILOT_ONLY", "ROUTE_TO_MANUAL_REVIEW", "REJECT_NEEDS_SOURCE", "REJECT_SCOPE_UNCLEAR", "REJECT_NOT_ENACTED", "REJECT_DUPLICATE", "DEFER_PENDING_LEGAL_REVIEW", "DRY_RUN_ONLY", "EXAMPLE_DO_NOT_IMPORT")
        };

    public Task<ProductionPolicyImportDryRunResult> DryRunAsync(
        ProductionPolicyImportDryRunRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var parseResult = ParseCsv(request.CsvContent ?? string.Empty);
        var globalFindings = new List<ProductionPolicyImportFinding>();
        var rowResults = new List<ProductionPolicyImportRowResult>();

        if (parseResult.Header.Count == 0)
        {
            globalFindings.Add(Fail("CSV is empty or missing a header row."));
            return Task.FromResult(BuildResult(request, rowResults, globalFindings));
        }

        ValidateHeader(parseResult.Header, globalFindings);

        if (parseResult.Rows.Count == 0)
        {
            globalFindings.Add(Pass("Template is header-only and contains no policy rows."));
            globalFindings.Add(Pass("No hard validation failures found."));
            return Task.FromResult(BuildResult(request, rowResults, globalFindings));
        }

        globalFindings.Add(Warn($"CSV contains {parseResult.Rows.Count} candidate row(s). This service does not import data."));

        var firstPolicyCodeRows = new Dictionary<string, int>(StringComparer.Ordinal);
        var firstActiveScopeRows = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var row in parseResult.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var findings = new List<ProductionPolicyImportFinding>();
            if (row.Fields.Count != parseResult.Header.Count)
            {
                findings.Add(Fail($"Row has {row.Fields.Count} fields; expected {parseResult.Header.Count}.", row.RowNumber));
                rowResults.Add(new ProductionPolicyImportRowResult(row.RowNumber, null, null, ProductionPolicyImportRowDecision.NOT_IMPORTABLE, findings));
                continue;
            }

            var values = ToRowMap(parseResult.Header, row.Fields);
            var candidate = ToCandidate(row.RowNumber, values);
            ValidateCandidate(candidate, findings, firstPolicyCodeRows, firstActiveScopeRows);

            rowResults.Add(new ProductionPolicyImportRowResult(
                row.RowNumber,
                NullIfBlank(candidate.PolicyCode),
                NullIfBlank(candidate.EntitlementType),
                Decide(candidate, findings),
                findings));
        }

        return Task.FromResult(BuildResult(request, rowResults, globalFindings));
    }

    private static void ValidateHeader(IReadOnlyList<string> header, List<ProductionPolicyImportFinding> findings)
    {
        var duplicateHeaders = header
            .GroupBy(static column => column, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1);

        foreach (var duplicate in duplicateHeaders)
        {
            findings.Add(Fail($"Duplicate header '{duplicate.Key}' found."));
        }

        foreach (var column in ImportColumns)
        {
            if (!header.Contains(column, StringComparer.Ordinal))
            {
                findings.Add(Fail($"Required header '{column}' is missing."));
            }
        }

        var allowedColumns = ImportColumns.Concat(ReviewColumns).ToHashSet(StringComparer.Ordinal);
        foreach (var column in header.Where(column => !allowedColumns.Contains(column)))
        {
            findings.Add(Warn($"Unexpected header '{column}' is present."));
        }

        if (header.Count != ImportColumns.Length && header.Count != ImportColumns.Length + ReviewColumns.Length)
        {
            findings.Add(Fail($"Header count is {header.Count}; expected {ImportColumns.Length} import columns or {ImportColumns.Length + ReviewColumns.Length} candidate worksheet columns."));
        }

        for (var i = 0; i < ImportColumns.Length; i++)
        {
            var found = i < header.Count ? header[i] : string.Empty;
            if (!string.Equals(found, ImportColumns[i], StringComparison.Ordinal))
            {
                findings.Add(Fail($"Header order mismatch at column {i + 1}: expected '{ImportColumns[i]}', found '{found}'."));
            }
        }

        if (header.Count == ImportColumns.Length + ReviewColumns.Length)
        {
            for (var i = 0; i < ReviewColumns.Length; i++)
            {
                var columnIndex = ImportColumns.Length + i;
                if (!string.Equals(header[columnIndex], ReviewColumns[i], StringComparison.Ordinal))
                {
                    findings.Add(Fail($"Candidate review header order mismatch at column {columnIndex + 1}: expected '{ReviewColumns[i]}', found '{header[columnIndex]}'."));
                }
            }
        }
    }

    private static void ValidateCandidate(
        ProductionPolicyImportCandidate candidate,
        List<ProductionPolicyImportFinding> findings,
        Dictionary<string, int> firstPolicyCodeRows,
        Dictionary<string, int> firstActiveScopeRows)
    {
        var rowText = string.Join("|", candidate.RawValues.Values);
        if (ContainsAnyMarker(rowText, "DRY_RUN_ONLY", "EXAMPLE_DO_NOT_IMPORT"))
        {
            findings.Add(Fail("Row is marked DRY_RUN_ONLY or EXAMPLE_DO_NOT_IMPORT and is not importable production policy data.", candidate.RowNumber));
        }

        foreach (var column in RequiredColumns)
        {
            if (IsBlank(GetValue(candidate.RawValues, column)))
            {
                findings.Add(Fail($"Required field '{column}' is blank.", candidate.RowNumber, column));
            }
        }

        if (!IsBlank(candidate.PolicyCode))
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(candidate.PolicyCode, "^[A-Z0-9][A-Z0-9_]{2,127}$"))
            {
                findings.Add(Fail("policy_code must be uppercase controlled code with no spaces.", candidate.RowNumber, "policy_code"));
            }

            if (ContainsAnyMarker(candidate.PolicyCode, "SANDBOX", "TEST", "DEV", "E2E", "DUMMY") || candidate.PolicyCode.StartsWith("EXAMPLE", StringComparison.Ordinal))
            {
                findings.Add(Fail("policy_code contains a sandbox/test/dev/dummy/example marker and cannot be used for production import.", candidate.RowNumber, "policy_code"));
            }

            if (firstPolicyCodeRows.TryGetValue(candidate.PolicyCode, out var firstRow))
            {
                findings.Add(Fail($"Duplicate policy_code '{candidate.PolicyCode}' also appears on row {firstRow}.", candidate.RowNumber, "policy_code"));
            }
            else
            {
                firstPolicyCodeRows[candidate.PolicyCode] = candidate.RowNumber;
            }
        }

        if (ContainsAnyMarker(candidate.PolicyName, "SANDBOX", "TEST", "DEV", "FIXTURE", "EXAMPLE", "DUMMY") ||
            ContainsAnyMarker(candidate.SourceReference, "SANDBOX", "TEST", "DEV", "FIXTURE", "EXAMPLE", "DUMMY") ||
            ContainsAnyMarker(candidate.LegalBasisReference, "SANDBOX", "TEST", "DEV", "FIXTURE", "EXAMPLE", "DUMMY") ||
            ContainsAnyMarker(candidate.OrdinanceReference, "SANDBOX", "TEST", "DEV", "FIXTURE", "EXAMPLE", "DUMMY") ||
            ContainsAnyMarker(candidate.NationalLawReference, "SANDBOX", "TEST", "DEV", "FIXTURE", "EXAMPLE", "DUMMY"))
        {
            findings.Add(Fail("Production row contains sandbox/test/dev/dummy/fixture/example marker in a policy reference field.", candidate.RowNumber));
        }

        foreach (var entry in AllowedValues)
        {
            var value = GetValue(candidate.RawValues, entry.Key);
            if (!IsBlank(value) && !entry.Value.Contains(value))
            {
                findings.Add(Fail($"Field '{entry.Key}' has invalid value '{value}'.", candidate.RowNumber, entry.Key));
            }
        }

        foreach (var column in BooleanColumns)
        {
            var value = GetValue(candidate.RawValues, column);
            if (!IsBlank(value) && value is not "true" and not "false")
            {
                findings.Add(Fail($"Boolean field '{column}' must be true or false.", candidate.RowNumber, column));
            }
        }

        var effectiveFromValid = TryParseDate(candidate.EffectiveFrom, out var effectiveFrom);
        if (!IsBlank(candidate.EffectiveFrom) && !effectiveFromValid)
        {
            findings.Add(Fail("effective_from is not a valid date.", candidate.RowNumber, "effective_from"));
        }

        var effectiveToValid = TryParseDate(candidate.EffectiveTo, out var effectiveTo);
        if (!IsBlank(candidate.EffectiveTo) && !effectiveToValid)
        {
            findings.Add(Fail("effective_to is not a valid date.", candidate.RowNumber, "effective_to"));
        }

        if (effectiveFromValid && effectiveToValid && effectiveTo <= effectiveFrom)
        {
            findings.Add(Fail("effective_to must be later than effective_from.", candidate.RowNumber, "effective_to"));
        }

        if (!IsBlank(candidate.FreeDurationMinutes) && !int.TryParse(candidate.FreeDurationMinutes, NumberStyles.None, CultureInfo.InvariantCulture, out _))
        {
            findings.Add(Fail("free_duration_minutes must be blank or a non-negative integer.", candidate.RowNumber, "free_duration_minutes"));
        }

        var requiresEvidence = candidate.RequiresEvidence == "true";
        if (requiresEvidence && IsBlank(candidate.RequiredEvidenceType))
        {
            findings.Add(Fail("required_evidence_type is required when requires_evidence is true.", candidate.RowNumber, "required_evidence_type"));
        }

        if (candidate.EntitlementType == "SENIOR_CITIZEN" && requiresEvidence && candidate.RequiredEvidenceType != "SENIOR_CITIZEN_ID")
        {
            findings.Add(Fail("SENIOR_CITIZEN policies requiring evidence must use SENIOR_CITIZEN_ID.", candidate.RowNumber, "required_evidence_type"));
        }

        if (candidate.EntitlementType == "PWD" && requiresEvidence && candidate.RequiredEvidenceType != "PWD_ID")
        {
            findings.Add(Fail("PWD policies requiring evidence must use PWD_ID.", candidate.RowNumber, "required_evidence_type"));
        }

        if (IsBlank(candidate.LegalBasisReference) && IsBlank(candidate.OrdinanceReference) && IsBlank(candidate.NationalLawReference))
        {
            findings.Add(Fail("At least one legal, ordinance, or national law reference is required.", candidate.RowNumber));
        }

        var isLocal = candidate.PolicyLevel == "LOCAL_ORDINANCE" || candidate.PolicyResolutionBasis == "LOCAL_ORDINANCE_APPLIED";
        if (isLocal)
        {
            if (IsBlank(candidate.OrdinanceReference))
            {
                findings.Add(Fail("Local ordinance rows require ordinance_reference.", candidate.RowNumber, "ordinance_reference"));
            }

            if (IsBlank(candidate.LguCode) && IsBlank(candidate.JurisdictionName) && IsBlank(candidate.SiteGroupCode) && IsBlank(candidate.SiteCode))
            {
                findings.Add(Fail("Local ordinance rows require LGU, jurisdiction, site group, or site scope.", candidate.RowNumber));
            }
        }

        var isNationalFallback = candidate.PolicyLevel == "NATIONAL_LAW" || candidate.PolicyResolutionBasis == "NATIONAL_LAW_FALLBACK";
        if (isNationalFallback)
        {
            if (IsBlank(candidate.NationalLawReference))
            {
                findings.Add(Fail("National fallback rows require national_law_reference.", candidate.RowNumber, "national_law_reference"));
            }

            if (candidate.EntitlementType == "SENIOR_CITIZEN" && candidate.NationalLawReference != "RA 9994")
            {
                findings.Add(Fail("SENIOR_CITIZEN national fallback rows require RA 9994.", candidate.RowNumber, "national_law_reference"));
            }

            if (candidate.EntitlementType == "PWD" && candidate.NationalLawReference != "RA 10754")
            {
                findings.Add(Fail("PWD national fallback rows require RA 10754.", candidate.RowNumber, "national_law_reference"));
            }
        }

        if (candidate.VerificationStatus is "VERIFIED_SECONDARY" or "VERIFIED_OFFICIAL" or "APPROVED_FOR_PILOT" or "ACTIVE_APPROVED")
        {
            if (IsBlank(candidate.ReviewedBy) || IsBlank(candidate.ReviewedAt))
            {
                findings.Add(Fail("Reviewed rows require reviewed_by and reviewed_at.", candidate.RowNumber));
            }
            else if (!TryParseDate(candidate.ReviewedAt, out _))
            {
                findings.Add(Fail("reviewed_at is not a valid timestamp.", candidate.RowNumber, "reviewed_at"));
            }
        }

        if (candidate.VerificationStatus is "APPROVED_FOR_PILOT" or "ACTIVE_APPROVED")
        {
            if (IsBlank(candidate.ApprovedBy) || IsBlank(candidate.ApprovedAt))
            {
                findings.Add(Fail("Approved rows require approved_by and approved_at.", candidate.RowNumber));
            }
            else if (!TryParseDate(candidate.ApprovedAt, out _))
            {
                findings.Add(Fail("approved_at is not a valid timestamp.", candidate.RowNumber, "approved_at"));
            }
        }

        if (candidate.VerificationStatus is "LEAD_UNVERIFIED" or "PROPOSED_ONLY" or "REJECTED")
        {
            findings.Add(Warn("Row is not eligible for production auto-application.", candidate.RowNumber, "verification_status"));
        }

        if (candidate.BeneficiaryResidencyScope is "MIXED_OR_CONFLICTING" or "UNVERIFIED")
        {
            findings.Add(Warn("Residency scope requires manual review.", candidate.RowNumber, "beneficiary_residency_scope"));
        }

        if (candidate.RequiresOperatorValidation == "false")
        {
            findings.Add(Warn("requires_operator_validation=false blocks controlled Operator Console production auto-application unless formally exempted.", candidate.RowNumber, "requires_operator_validation"));
        }

        if (candidate.BenefitType == "FREE_DURATION" && IsBlank(candidate.FreeDurationMinutes))
        {
            findings.Add(Fail("FREE_DURATION requires free_duration_minutes.", candidate.RowNumber, "free_duration_minutes"));
        }

        if (candidate.BenefitType == "INITIAL_RATE_EXEMPTION" && candidate.InitialRateExempt != "true")
        {
            findings.Add(Fail("INITIAL_RATE_EXEMPTION requires initial_rate_exempt=true.", candidate.RowNumber, "initial_rate_exempt"));
        }

        if (candidate.BenefitType == "FULL_FEE_EXEMPTION" && candidate.FullFeeExempt != "true")
        {
            findings.Add(Fail("FULL_FEE_EXEMPTION requires full_fee_exempt=true.", candidate.RowNumber, "full_fee_exempt"));
        }

        if (candidate.ReviewStatus == "APPROVE_FOR_IMPORT" && candidate.VerificationStatus != "ACTIVE_APPROVED")
        {
            findings.Add(Fail("APPROVE_FOR_IMPORT requires verification_status=ACTIVE_APPROVED.", candidate.RowNumber, "review_status"));
        }

        if (candidate.ReviewStatus == "APPROVE_FOR_PILOT_ONLY" && candidate.VerificationStatus != "APPROVED_FOR_PILOT")
        {
            findings.Add(Warn("APPROVE_FOR_PILOT_ONLY should use verification_status=APPROVED_FOR_PILOT.", candidate.RowNumber, "review_status"));
        }

        if (candidate.VerificationStatus == "ACTIVE_APPROVED")
        {
            var scopeKey = string.Join(
                "|",
                candidate.EntitlementType,
                candidate.LguCode,
                candidate.JurisdictionName,
                candidate.SiteGroupCode,
                candidate.SiteCode,
                candidate.EffectiveFrom,
                candidate.EffectiveTo);

            if (firstActiveScopeRows.TryGetValue(scopeKey, out var firstRow))
            {
                findings.Add(Fail($"Duplicate active-approved entitlement/scope/effective-period also appears on row {firstRow}.", candidate.RowNumber));
            }
            else
            {
                firstActiveScopeRows[scopeKey] = candidate.RowNumber;
            }
        }

        if (ContainsAnyMarker(candidate.Notes, "PASSWORD", "PRIVATE KEY", "SECRET", "CREDENTIAL", "RAW EVIDENCE", "ID NUMBER", "PASSPORT", "DRIVER LICENSE"))
        {
            findings.Add(Warn("notes may contain sensitive data and must be reviewed.", candidate.RowNumber, "notes"));
        }
    }

    private static ProductionPolicyImportRowDecision Decide(
        ProductionPolicyImportCandidate candidate,
        IReadOnlyCollection<ProductionPolicyImportFinding> findings)
    {
        if (findings.Any(static finding => finding.Severity == ProductionPolicyImportFindingSeverity.FAIL &&
            finding.Message.Contains("Duplicate policy_code", StringComparison.Ordinal)))
        {
            return ProductionPolicyImportRowDecision.DUPLICATE_IN_FILE;
        }

        if (findings.Any(static finding => finding.Severity == ProductionPolicyImportFindingSeverity.FAIL &&
            finding.Message.Contains("DRY_RUN_ONLY", StringComparison.Ordinal)))
        {
            return ProductionPolicyImportRowDecision.DRY_RUN_ONLY;
        }

        if (findings.Any(static finding => finding.Severity == ProductionPolicyImportFindingSeverity.FAIL))
        {
            return ProductionPolicyImportRowDecision.NOT_IMPORTABLE;
        }

        if (findings.Any(static finding => finding.Severity == ProductionPolicyImportFindingSeverity.WARN) ||
            candidate.ReviewStatus is "ROUTE_TO_MANUAL_REVIEW" or "APPROVE_FOR_PILOT_ONLY" or "DEFER_PENDING_LEGAL_REVIEW")
        {
            return ProductionPolicyImportRowDecision.MANUAL_REVIEW_REQUIRED;
        }

        return ProductionPolicyImportRowDecision.IMPORTABLE_AFTER_APPROVAL;
    }

    private static ProductionPolicyImportDryRunResult BuildResult(
        ProductionPolicyImportDryRunRequest request,
        IReadOnlyList<ProductionPolicyImportRowResult> rows,
        IReadOnlyList<ProductionPolicyImportFinding> findings)
    {
        var allFindings = findings.Concat(rows.SelectMany(static row => row.Findings)).ToArray();
        var passCount = allFindings.Count(static finding => finding.Severity == ProductionPolicyImportFindingSeverity.PASS);
        var warnCount = allFindings.Count(static finding => finding.Severity == ProductionPolicyImportFindingSeverity.WARN);
        var failCount = allFindings.Count(static finding => finding.Severity == ProductionPolicyImportFindingSeverity.FAIL);

        return new ProductionPolicyImportDryRunResult(
            IsDryRun: true,
            PoliciesImported: false,
            TotalRows: rows.Count,
            ImportableRows: rows.Count(static row => row.Decision == ProductionPolicyImportRowDecision.IMPORTABLE_AFTER_APPROVAL),
            ManualReviewRows: rows.Count(static row => row.Decision == ProductionPolicyImportRowDecision.MANUAL_REVIEW_REQUIRED),
            NotImportableRows: rows.Count(static row => row.Decision == ProductionPolicyImportRowDecision.NOT_IMPORTABLE),
            DryRunOnlyRows: rows.Count(static row => row.Decision == ProductionPolicyImportRowDecision.DRY_RUN_ONLY),
            DuplicateRows: rows.Count(static row => row.Decision == ProductionPolicyImportRowDecision.DUPLICATE_IN_FILE),
            passCount,
            warnCount,
            failCount,
            rows,
            findings,
            request.CorrelationId);
    }

    private static ProductionPolicyImportCandidate ToCandidate(int rowNumber, IReadOnlyDictionary<string, string> values) =>
        new(
            rowNumber,
            GetValue(values, "policy_code"),
            GetValue(values, "policy_name"),
            GetValue(values, "entitlement_type"),
            GetValue(values, "lgu_code"),
            GetValue(values, "jurisdiction_name"),
            GetValue(values, "site_group_code"),
            GetValue(values, "site_code"),
            GetValue(values, "policy_level"),
            GetValue(values, "policy_type"),
            GetValue(values, "policy_resolution_basis"),
            GetValue(values, "benefit_type"),
            GetValue(values, "discount_base_scope"),
            GetValue(values, "free_duration_minutes"),
            GetValue(values, "initial_rate_exempt"),
            GetValue(values, "full_fee_exempt"),
            GetValue(values, "overnight_excluded"),
            GetValue(values, "valet_excluded"),
            GetValue(values, "standalone_parking_excluded"),
            GetValue(values, "driver_or_passenger_required"),
            GetValue(values, "beneficiary_residency_scope"),
            GetValue(values, "requires_evidence"),
            GetValue(values, "required_evidence_type"),
            GetValue(values, "requires_operator_validation"),
            GetValue(values, "legal_basis_reference"),
            GetValue(values, "ordinance_reference"),
            GetValue(values, "national_law_reference"),
            GetValue(values, "source_reference"),
            GetValue(values, "verification_status"),
            GetValue(values, "effective_from"),
            GetValue(values, "effective_to"),
            GetValue(values, "reviewed_by"),
            GetValue(values, "reviewed_at"),
            GetValue(values, "approved_by"),
            GetValue(values, "approved_at"),
            GetValue(values, "notes"),
            GetNullableValue(values, "review_status"),
            GetNullableValue(values, "review_owner"),
            GetNullableValue(values, "legal_review_decision"),
            GetNullableValue(values, "product_review_decision"),
            GetNullableValue(values, "ops_review_decision"),
            GetNullableValue(values, "engineering_review_decision"),
            GetNullableValue(values, "qa_review_decision"),
            GetNullableValue(values, "approval_notes"),
            values);

    private static IReadOnlyDictionary<string, string> ToRowMap(IReadOnlyList<string> header, IReadOnlyList<string> fields)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < header.Count; i++)
        {
            values[header[i]] = i < fields.Count ? fields[i].Trim() : string.Empty;
        }

        return values;
    }

    private static CsvParseResult ParseCsv(string csvContent)
    {
        using var reader = new StringReader(csvContent);
        var records = new List<CsvRecord>();
        var rowNumber = 0;
        string? line;
        var pending = new StringBuilder();
        var pendingRowNumber = 0;

        while ((line = reader.ReadLine()) is not null)
        {
            rowNumber++;
            if (pending.Length == 0)
            {
                pendingRowNumber = rowNumber;
            }
            else
            {
                pending.AppendLine();
            }

            pending.Append(line);
            if (!HasBalancedQuotes(pending.ToString()))
            {
                continue;
            }

            var fields = ParseCsvRecord(pending.ToString());
            pending.Clear();

            if (fields.Count == 1 && IsBlank(fields[0]))
            {
                continue;
            }

            records.Add(new CsvRecord(pendingRowNumber, fields));
        }

        if (pending.Length > 0)
        {
            records.Add(new CsvRecord(pendingRowNumber, ParseCsvRecord(pending.ToString())));
        }

        if (records.Count == 0)
        {
            return new CsvParseResult([], []);
        }

        var header = records[0].Fields.Select(static field => field.Trim()).ToArray();
        var rows = records.Skip(1).ToArray();
        return new CsvParseResult(header, rows);
    }

    private static IReadOnlyList<string> ParseCsvRecord(string record)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < record.Length; i++)
        {
            var c = record[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < record.Length && record[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                    continue;
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(c);
        }

        fields.Add(current.ToString());
        return fields;
    }

    private static bool HasBalancedQuotes(string value)
    {
        var quoteCount = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '"')
            {
                continue;
            }

            if (i + 1 < value.Length && value[i + 1] == '"')
            {
                i++;
                continue;
            }

            quoteCount++;
        }

        return quoteCount % 2 == 0;
    }

    private static bool TryParseDate(string value, out DateTimeOffset parsed)
    {
        if (IsBlank(value))
        {
            parsed = default;
            return false;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out parsed);
    }

    private static bool ContainsAnyMarker(string? value, params string[] markers)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetValue(IReadOnlyDictionary<string, string> values, string column) =>
        values.TryGetValue(column, out var value) ? value.Trim() : string.Empty;

    private static string? GetNullableValue(IReadOnlyDictionary<string, string> values, string column) =>
        values.TryGetValue(column, out var value) ? value.Trim() : null;

    private static string? NullIfBlank(string value) => IsBlank(value) ? null : value;

    private static bool IsBlank(string? value) => string.IsNullOrWhiteSpace(value);

    private static ProductionPolicyImportFinding Pass(string message, int? rowNumber = null, string? field = null) =>
        new(ProductionPolicyImportFindingSeverity.PASS, message, rowNumber, field);

    private static ProductionPolicyImportFinding Warn(string message, int? rowNumber = null, string? field = null) =>
        new(ProductionPolicyImportFindingSeverity.WARN, message, rowNumber, field);

    private static ProductionPolicyImportFinding Fail(string message, int? rowNumber = null, string? field = null) =>
        new(ProductionPolicyImportFindingSeverity.FAIL, message, rowNumber, field);

    private static IReadOnlySet<string> Set(params string[] values) => values.ToHashSet(StringComparer.Ordinal);

    private sealed record CsvParseResult(IReadOnlyList<string> Header, IReadOnlyList<CsvRecord> Rows);

    private sealed record CsvRecord(int RowNumber, IReadOnlyList<string> Fields);
}
