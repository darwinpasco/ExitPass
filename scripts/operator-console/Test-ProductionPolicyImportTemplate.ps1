param(
    [Parameter(Mandatory = $true)]
    [string] $Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$expectedColumns = @(
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
)

$requiredColumns = @(
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
)

$allowed = @{
    entitlement_type = @("SENIOR_CITIZEN", "PWD", "OTHER_STATUTORY")
    policy_level = @("NATIONAL_LAW", "LOCAL_ORDINANCE", "SITE_POLICY", "OPERATIONAL_POLICY")
    policy_type = @("LEGAL_REFERENCE", "LOCAL_ORDINANCE", "SITE_POLICY", "OPERATIONAL_POLICY", "IMPLEMENTATION_POLICY")
    policy_resolution_basis = @("LOCAL_ORDINANCE_APPLIED", "NATIONAL_LAW_FALLBACK", "SITE_POLICY_OPERATIONAL_ONLY", "MANUAL_POLICY_SELECTION", "SYSTEM_DEFAULT")
    benefit_type = @("STATUTORY_DISCOUNT_VAT_EXEMPT", "FREE_DURATION", "INITIAL_RATE_EXEMPTION", "FULL_FEE_EXEMPTION", "LOCAL_RULE", "MANUAL_REVIEW")
    discount_base_scope = @("VAT_EXCLUSIVE", "GROSS", "NET", "NOT_APPLICABLE")
    beneficiary_residency_scope = @("RESIDENT_ONLY", "NON_RESIDENT_ALLOWED", "MIXED_OR_CONFLICTING", "UNVERIFIED", "NOT_APPLICABLE")
    required_evidence_type = @("SENIOR_CITIZEN_ID", "PWD_ID", "AUTHORIZATION_LETTER", "SUPPORTING_DOCUMENT", "VALIDATION_SCREENSHOT", "HASH_ONLY_REFERENCE", "OTHER")
    verification_status = @("LEAD_UNVERIFIED", "VERIFIED_SECONDARY", "VERIFIED_OFFICIAL", "APPROVED_FOR_PILOT", "ACTIVE_APPROVED", "PROPOSED_ONLY", "REJECTED")
}

$booleanColumns = @(
    "initial_rate_exempt",
    "full_fee_exempt",
    "overnight_excluded",
    "valet_excluded",
    "standalone_parking_excluded",
    "driver_or_passenger_required",
    "requires_evidence",
    "requires_operator_validation"
)

$findings = [System.Collections.Generic.List[object]]::new()

function Add-Finding {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("PASS", "WARN", "FAIL")]
        [string] $Level,
        [Parameter(Mandatory = $true)]
        [string] $Message,
        [int] $RowNumber = 0
    )

    $script:findings.Add([pscustomobject]@{
        Level = $Level
        Row = $RowNumber
        Message = $Message
    }) | Out-Null
}

function Is-Blank {
    param([AllowNull()][string] $Value)
    return [string]::IsNullOrWhiteSpace($Value)
}

function Normalize-Field {
    param([AllowNull()][string] $Value)
    if ($null -eq $Value) {
        return ""
    }

    return $Value.Trim()
}

function Test-DateValue {
    param([string] $Value)
    $parsed = [datetime]::MinValue
    return [datetime]::TryParse($Value, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal, [ref] $parsed)
}

function Read-CsvRows {
    param([string] $CsvPath)

    Add-Type -AssemblyName Microsoft.VisualBasic
    $parser = [Microsoft.VisualBasic.FileIO.TextFieldParser]::new($CsvPath)
    $parser.TextFieldType = [Microsoft.VisualBasic.FileIO.FieldType]::Delimited
    $parser.SetDelimiters(",")
    $parser.HasFieldsEnclosedInQuotes = $true

    try {
        $header = $null
        while (-not $parser.EndOfData -and $null -eq $header) {
            $candidate = $parser.ReadFields()
            if ($null -ne $candidate -and -not (($candidate.Count -eq 1) -and [string]::IsNullOrWhiteSpace($candidate[0]))) {
                $header = $candidate
            }
        }

        $rows = [System.Collections.Generic.List[object]]::new()
        $rowNumber = 1
        while (-not $parser.EndOfData) {
            $rowNumber++
            $fields = $parser.ReadFields()
            if ($null -eq $fields -or (($fields.Count -eq 1) -and [string]::IsNullOrWhiteSpace($fields[0]))) {
                continue
            }

            $rows.Add([pscustomobject]@{
                RowNumber = $rowNumber
                Fields = $fields
            }) | Out-Null
        }

        return [pscustomobject]@{
            Header = $header
            Rows = $rows
        }
    }
    finally {
        $parser.Close()
    }
}

function Get-RowMap {
    param(
        [string[]] $Header,
        [string[]] $Fields
    )

    $map = @{}
    for ($i = 0; $i -lt $Header.Count; $i++) {
        $value = ""
        if ($i -lt $Fields.Count) {
            $value = Normalize-Field $Fields[$i]
        }

        $map[$Header[$i]] = $value
    }

    return $map
}

$resolvedPath = Resolve-Path -LiteralPath $Path -ErrorAction Stop
$csv = Read-CsvRows -CsvPath $resolvedPath.Path

if ($null -eq $csv.Header) {
    Add-Finding -Level "FAIL" -Message "CSV is empty or missing a header row."
}
else {
    $header = @($csv.Header | ForEach-Object { Normalize-Field $_ })

    $duplicateHeaders = $header | Group-Object | Where-Object { $_.Count -gt 1 }
    foreach ($duplicate in $duplicateHeaders) {
        Add-Finding -Level "FAIL" -Message "Duplicate header '$($duplicate.Name)' found."
    }

    foreach ($column in $expectedColumns) {
        if (-not ($header -contains $column)) {
            Add-Finding -Level "FAIL" -Message "Required header '$column' is missing."
        }
    }

    $unexpectedHeaders = $header | Where-Object { -not ($expectedColumns -contains $_) }
    foreach ($column in $unexpectedHeaders) {
        Add-Finding -Level "WARN" -Message "Unexpected header '$column' is present."
    }

    if ($header.Count -ne $expectedColumns.Count) {
        Add-Finding -Level "FAIL" -Message "Header count is $($header.Count); expected $($expectedColumns.Count)."
    }
    else {
        for ($i = 0; $i -lt $expectedColumns.Count; $i++) {
            if ($header[$i] -ne $expectedColumns[$i]) {
                Add-Finding -Level "FAIL" -Message "Header order mismatch at column $($i + 1): expected '$($expectedColumns[$i])', found '$($header[$i])'."
            }
        }
    }

    if ($csv.Rows.Count -eq 0) {
        Add-Finding -Level "PASS" -Message "Template is header-only and contains no policy rows."
    }
    else {
        Add-Finding -Level "WARN" -Message "CSV contains $($csv.Rows.Count) candidate row(s). This validator does not import data."
    }

    $policyCodes = @{}
    $activeScopes = @{}

    foreach ($row in $csv.Rows) {
        if ($row.Fields.Count -ne $header.Count) {
            Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "Row has $($row.Fields.Count) fields; expected $($header.Count)."
            continue
        }

        $map = Get-RowMap -Header $header -Fields $row.Fields

        foreach ($column in $requiredColumns) {
            if (Is-Blank $map[$column]) {
                Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "Required field '$column' is blank."
            }
        }

        $policyCode = $map["policy_code"]
        if (-not (Is-Blank $policyCode)) {
            if ($policyCode -notmatch "^[A-Z0-9][A-Z0-9_]{2,127}$") {
                Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "policy_code must be uppercase controlled code with no spaces."
            }

            if ($policyCode -match "(SANDBOX|TEST|DEV|E2E)" -or $policyCode -match "^EXAMPLE") {
                Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "policy_code contains a sandbox/test/dev/example marker and cannot be used for production import."
            }

            if ($policyCodes.ContainsKey($policyCode)) {
                Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "Duplicate policy_code '$policyCode' also appears on row $($policyCodes[$policyCode])."
            }
            else {
                $policyCodes[$policyCode] = $row.RowNumber
            }
        }

        if (($map["policy_name"] -match "(?i)(sandbox|test|dev|fixture|example)") -or
            ($map["source_reference"] -match "(?i)(sandbox|test|dev|fixture|example)") -or
            ($map["legal_basis_reference"] -match "(?i)(sandbox|test|dev|fixture|example)") -or
            ($map["ordinance_reference"] -match "(?i)(sandbox|test|dev|fixture|example)") -or
            ($map["national_law_reference"] -match "(?i)(sandbox|test|dev|fixture|example)")) {
            Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "Production row contains sandbox/test/dev/fixture/example marker in a policy reference field."
        }

        foreach ($entry in $allowed.GetEnumerator()) {
            $column = $entry.Key
            $value = $map[$column]
            if (-not (Is-Blank $value) -and -not ($entry.Value -contains $value)) {
                Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "Field '$column' has invalid value '$value'."
            }
        }

        foreach ($column in $booleanColumns) {
            $value = $map[$column]
            if (-not (Is-Blank $value) -and $value -notin @("true", "false")) {
                Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "Boolean field '$column' must be true or false."
            }
        }

        if (-not (Is-Blank $map["effective_from"]) -and -not (Test-DateValue $map["effective_from"])) {
            Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "effective_from is not a valid date."
        }

        if (-not (Is-Blank $map["effective_to"]) -and -not (Test-DateValue $map["effective_to"])) {
            Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "effective_to is not a valid date."
        }

        if ((Test-DateValue $map["effective_from"]) -and -not (Is-Blank $map["effective_to"]) -and (Test-DateValue $map["effective_to"])) {
            if ([datetime]$map["effective_to"] -le [datetime]$map["effective_from"]) {
                Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "effective_to must be later than effective_from."
            }
        }

        if (-not (Is-Blank $map["free_duration_minutes"]) -and ($map["free_duration_minutes"] -notmatch "^\d+$")) {
            Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "free_duration_minutes must be blank or a non-negative integer."
        }

        $requiresEvidence = $map["requires_evidence"] -eq "true"
        if ($requiresEvidence -and (Is-Blank $map["required_evidence_type"])) {
            Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "required_evidence_type is required when requires_evidence is true."
        }

        if ($map["entitlement_type"] -eq "SENIOR_CITIZEN" -and $requiresEvidence -and $map["required_evidence_type"] -ne "SENIOR_CITIZEN_ID") {
            Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "SENIOR_CITIZEN policies requiring evidence must use SENIOR_CITIZEN_ID."
        }

        if ($map["entitlement_type"] -eq "PWD" -and $requiresEvidence -and $map["required_evidence_type"] -ne "PWD_ID") {
            Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "PWD policies requiring evidence must use PWD_ID."
        }

        if ((Is-Blank $map["legal_basis_reference"]) -and (Is-Blank $map["ordinance_reference"]) -and (Is-Blank $map["national_law_reference"])) {
            Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "At least one legal, ordinance, or national law reference is required."
        }

        $isLocal = $map["policy_level"] -eq "LOCAL_ORDINANCE" -or $map["policy_resolution_basis"] -eq "LOCAL_ORDINANCE_APPLIED"
        if ($isLocal) {
            if (Is-Blank $map["ordinance_reference"]) {
                Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "Local ordinance rows require ordinance_reference."
            }

            if ((Is-Blank $map["lgu_code"]) -and (Is-Blank $map["jurisdiction_name"]) -and (Is-Blank $map["site_group_code"]) -and (Is-Blank $map["site_code"])) {
                Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "Local ordinance rows require LGU, jurisdiction, site group, or site scope."
            }
        }

        $isNationalFallback = $map["policy_level"] -eq "NATIONAL_LAW" -or $map["policy_resolution_basis"] -eq "NATIONAL_LAW_FALLBACK"
        if ($isNationalFallback) {
            if (Is-Blank $map["national_law_reference"]) {
                Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "National fallback rows require national_law_reference."
            }

            if ($map["entitlement_type"] -eq "SENIOR_CITIZEN" -and $map["national_law_reference"] -ne "RA 9994") {
                Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "SENIOR_CITIZEN national fallback rows require RA 9994."
            }

            if ($map["entitlement_type"] -eq "PWD" -and $map["national_law_reference"] -ne "RA 10754") {
                Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "PWD national fallback rows require RA 10754."
            }
        }

        if ($map["verification_status"] -in @("VERIFIED_SECONDARY", "VERIFIED_OFFICIAL", "APPROVED_FOR_PILOT", "ACTIVE_APPROVED")) {
            if ((Is-Blank $map["reviewed_by"]) -or (Is-Blank $map["reviewed_at"])) {
                Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "Reviewed rows require reviewed_by and reviewed_at."
            }
            elseif (-not (Test-DateValue $map["reviewed_at"])) {
                Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "reviewed_at is not a valid timestamp."
            }
        }

        if ($map["verification_status"] -in @("APPROVED_FOR_PILOT", "ACTIVE_APPROVED")) {
            if ((Is-Blank $map["approved_by"]) -or (Is-Blank $map["approved_at"])) {
                Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "Approved rows require approved_by and approved_at."
            }
            elseif (-not (Test-DateValue $map["approved_at"])) {
                Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "approved_at is not a valid timestamp."
            }
        }

        if ($map["verification_status"] -in @("LEAD_UNVERIFIED", "PROPOSED_ONLY", "REJECTED")) {
            Add-Finding -Level "WARN" -RowNumber $row.RowNumber -Message "Row is not eligible for production auto-application."
        }

        if ($map["beneficiary_residency_scope"] -in @("MIXED_OR_CONFLICTING", "UNVERIFIED")) {
            Add-Finding -Level "WARN" -RowNumber $row.RowNumber -Message "Residency scope requires manual review."
        }

        if ($map["requires_operator_validation"] -eq "false") {
            Add-Finding -Level "WARN" -RowNumber $row.RowNumber -Message "requires_operator_validation=false blocks controlled Operator Console production auto-application unless formally exempted."
        }

        if ($map["benefit_type"] -eq "FREE_DURATION" -and (Is-Blank $map["free_duration_minutes"])) {
            Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "FREE_DURATION requires free_duration_minutes."
        }

        if ($map["benefit_type"] -eq "INITIAL_RATE_EXEMPTION" -and $map["initial_rate_exempt"] -ne "true") {
            Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "INITIAL_RATE_EXEMPTION requires initial_rate_exempt=true."
        }

        if ($map["benefit_type"] -eq "FULL_FEE_EXEMPTION" -and $map["full_fee_exempt"] -ne "true") {
            Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "FULL_FEE_EXEMPTION requires full_fee_exempt=true."
        }

        if ($map["verification_status"] -eq "ACTIVE_APPROVED") {
            $scopeKey = @(
                $map["entitlement_type"],
                $map["lgu_code"],
                $map["jurisdiction_name"],
                $map["site_group_code"],
                $map["site_code"],
                $map["effective_from"],
                $map["effective_to"]
            ) -join "|"

            if ($activeScopes.ContainsKey($scopeKey)) {
                Add-Finding -Level "FAIL" -RowNumber $row.RowNumber -Message "Duplicate active-approved entitlement/scope/effective-period also appears on row $($activeScopes[$scopeKey])."
            }
            else {
                $activeScopes[$scopeKey] = $row.RowNumber
            }
        }

        if ($map["notes"] -match "(?i)(password|private key|secret|credential|raw evidence|id number|passport|driver.?license)") {
            Add-Finding -Level "WARN" -RowNumber $row.RowNumber -Message "notes may contain sensitive data and must be reviewed."
        }
    }
}

if (-not ($findings | Where-Object { $_.Level -eq "FAIL" })) {
    Add-Finding -Level "PASS" -Message "No hard validation failures found."
}

foreach ($finding in $findings) {
    $rowPrefix = ""
    if ($finding.Row -gt 0) {
        $rowPrefix = " row=$($finding.Row)"
    }

    Write-Host "$($finding.Level):$rowPrefix $($finding.Message)"
}

if ($findings | Where-Object { $_.Level -eq "FAIL" }) {
    exit 1
}

exit 0
