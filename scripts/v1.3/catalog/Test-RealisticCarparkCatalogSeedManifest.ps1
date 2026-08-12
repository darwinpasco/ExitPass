[CmdletBinding()]
param(
    [string]$WorkbookPath = 'D:\Docs\Carparks.xlsx',
    [string]$RepositoryRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:Checks = 0
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    $script:Checks++
    if (-not $Condition) { throw "Manifest validation failed: $Message" }
}

function Assert-Equal {
    param($Actual, $Expected, [string]$Message)
    Assert-True ([string]$Actual -ceq [string]$Expected) "$Message (expected '$Expected', observed '$Actual')"
}

function Normalize-WorkbookValue {
    param([string]$Value)
    if ($null -eq $Value) { return '' }
    return (($Value.Trim()) -replace '\s+', ' ')
}

function Get-CatalogCode {
    param([string]$Value)
    return ((Normalize-WorkbookValue $Value).ToUpperInvariant() -replace '[^A-Z0-9]+', '-').Trim('-')
}

function New-UuidV5 {
    param([Guid]$Namespace, [string]$Name)
    $namespaceBytes = $Namespace.ToByteArray()
    [Array]::Reverse($namespaceBytes, 0, 4)
    [Array]::Reverse($namespaceBytes, 4, 2)
    [Array]::Reverse($namespaceBytes, 6, 2)
    $nameBytes = [Text.Encoding]::UTF8.GetBytes($Name)
    $sha1 = [Security.Cryptography.SHA1]::Create()
    try { $hash = $sha1.ComputeHash($namespaceBytes + $nameBytes) } finally { $sha1.Dispose() }
    $bytes = [byte[]]$hash[0..15]
    $bytes[6] = ($bytes[6] -band 0x0f) -bor 0x50
    $bytes[8] = ($bytes[8] -band 0x3f) -bor 0x80
    [Array]::Reverse($bytes, 0, 4)
    [Array]::Reverse($bytes, 4, 2)
    [Array]::Reverse($bytes, 6, 2)
    return (New-Object Guid (,$bytes)).ToString()
}

function Get-ZipEntryText {
    param($Archive, [string]$Name)
    $entry = $Archive.GetEntry($Name)
    Assert-True ($null -ne $entry) "Workbook entry '$Name' is missing"
    $reader = New-Object IO.StreamReader($entry.Open(), [Text.Encoding]::UTF8, $true)
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}

function Get-WorkbookInventory {
    param([string]$Path)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        [xml]$workbook = Get-ZipEntryText $archive 'xl/workbook.xml'
        [xml]$relationships = Get-ZipEntryText $archive 'xl/_rels/workbook.xml.rels'
        $wbNs = New-Object Xml.XmlNamespaceManager($workbook.NameTable)
        $wbNs.AddNamespace('m', 'http://schemas.openxmlformats.org/spreadsheetml/2006/main')
        $wbNs.AddNamespace('r', 'http://schemas.openxmlformats.org/officeDocument/2006/relationships')
        $relMap = @{}
        foreach ($rel in $relationships.Relationships.Relationship) { $relMap[[string]$rel.Id] = [string]$rel.Target }

        $sharedStrings = @()
        $sharedEntry = $archive.GetEntry('xl/sharedStrings.xml')
        if ($null -ne $sharedEntry) {
            [xml]$sharedXml = Get-ZipEntryText $archive 'xl/sharedStrings.xml'
            $sharedNs = New-Object Xml.XmlNamespaceManager($sharedXml.NameTable)
            $sharedNs.AddNamespace('m', 'http://schemas.openxmlformats.org/spreadsheetml/2006/main')
            foreach ($item in $sharedXml.SelectNodes('//m:si', $sharedNs)) {
                $parts = @($item.SelectNodes('.//m:t', $sharedNs) | ForEach-Object { $_.'#text' })
                $sharedStrings += ($parts -join '')
            }
        }

        $sheets = @()
        foreach ($sheet in $workbook.SelectNodes('//m:sheets/m:sheet', $wbNs)) {
            $target = $relMap[[string]$sheet.GetAttribute('id', 'http://schemas.openxmlformats.org/officeDocument/2006/relationships')]
            $target = $target.Replace('\', '/').TrimStart('/')
            if (-not $target.StartsWith('xl/')) { $target = 'xl/' + $target }
            [xml]$sheetXml = Get-ZipEntryText $archive $target
            $sheetNs = New-Object Xml.XmlNamespaceManager($sheetXml.NameTable)
            $sheetNs.AddNamespace('m', 'http://schemas.openxmlformats.org/spreadsheetml/2006/main')
            $rows = @()
            foreach ($row in $sheetXml.SelectNodes('//m:sheetData/m:row', $sheetNs)) {
                $values = @{}
                foreach ($cell in $row.SelectNodes('./m:c', $sheetNs)) {
                    $column = ([regex]::Match([string]$cell.r, '^[A-Z]+')).Value
                    $cellType = [string]$cell.t
                    $valueNode = $cell.SelectSingleNode('./m:v', $sheetNs)
                    $value = if ($null -eq $valueNode) { '' } else { [string]$valueNode.InnerText }
                    if ($cellType -eq 's' -and $value -ne '') { $value = [string]$sharedStrings[[int]$value] }
                    elseif ($cellType -eq 'inlineStr') {
                        $value = (@($cell.SelectNodes('.//m:t', $sheetNs) | ForEach-Object { $_.'#text' }) -join '')
                    }
                    $values[$column] = $value
                }
                $rows += [pscustomobject]@{ RowNumber=[int]$row.r; A=[string]$values['A']; B=[string]$values['B']; C=[string]$values['C'] }
            }
            $dimension = $sheetXml.SelectSingleNode('//m:dimension', $sheetNs)
            $sheets += [pscustomobject]@{
                Name=[string]$sheet.name
                State=if ([string]::IsNullOrWhiteSpace([string]$sheet.GetAttribute('state'))) { 'visible' } else { [string]$sheet.GetAttribute('state') }
                UsedRange=if ($null -eq $dimension) { '' } else { [string]$dimension.ref }
                Rows=$rows
                HiddenRows=@($sheetXml.SelectNodes('//m:sheetData/m:row[@hidden="1"]', $sheetNs)).Count
                HiddenColumns=@($sheetXml.SelectNodes('//m:cols/m:col[@hidden="1"]', $sheetNs)).Count
                MergedCells=@($sheetXml.SelectNodes('//m:mergeCells/m:mergeCell', $sheetNs)).Count
                FormulaCells=@($sheetXml.SelectNodes('//m:c/m:f', $sheetNs)).Count
            }
        }
        return $sheets
    }
    finally { $archive.Dispose() }
}

function Import-ManifestCsv {
    param([string]$Path, [string[]]$ExpectedHeaders)
    Assert-True (Test-Path -LiteralPath $Path -PathType Leaf) "Required file is missing: $Path"
    $bytes = [IO.File]::ReadAllBytes($Path)
    $strictUtf8 = New-Object Text.UTF8Encoding($false, $true)
    try { [void]$strictUtf8.GetString($bytes) } catch { throw "Manifest validation failed: '$Path' is not valid UTF-8" }
    $firstLine = [IO.File]::ReadLines($Path) | Select-Object -First 1
    $headers = @($firstLine -split ',' | ForEach-Object { $_.Trim('"') })
    Assert-Equal ($headers -join '|') ($ExpectedHeaders -join '|') "CSV headers differ for '$Path'"
    return @(Import-Csv -LiteralPath $Path -Encoding UTF8)
}

$manifestRoot = Join-Path $RepositoryRoot 'docs\v1.3\central-pms\seed-manifests'
$dataRoot = Join-Path $manifestRoot 'data'
$mainDoc = Join-Path $manifestRoot 'ExitPass_Realistic_Carpark_Catalog_and_Jurisdiction_Seed_Manifest_v1.0.md'
$reconciliationDoc = Join-Path $manifestRoot 'ExitPass_Realistic_Carpark_Fixture_Identity_Reconciliation_v1.0.md'
$groupPath = Join-Path $dataRoot 'ExitPass_Realistic_Carpark_Site_Groups_v1.0.csv'
$sitePath = Join-Path $dataRoot 'ExitPass_Realistic_Carpark_Sites_v1.0.csv'
$assignmentPath = Join-Path $dataRoot 'ExitPass_Realistic_Carpark_Site_Jurisdiction_Assignments_v1.0.csv'
$coveragePath = Join-Path $dataRoot 'ExitPass_Realistic_Carpark_Statutory_Discount_Coverage_v1.0.csv'
$sourcePath = Join-Path $dataRoot 'ExitPass_Realistic_Carpark_Source_Register_v1.0.csv'

foreach ($path in @($mainDoc,$reconciliationDoc,$groupPath,$sitePath,$assignmentPath,$coveragePath,$sourcePath)) {
    Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "Required deliverable is missing: $path"
}

Assert-True (Test-Path -LiteralPath $WorkbookPath -PathType Leaf) "Authoritative workbook is missing"
$expectedWorkbookHash = '63c20cd3aba3e13d6f9fc022083507c0bc43a2ab9c751e9084dd19c59969359a'
Assert-Equal ((Get-FileHash -LiteralPath $WorkbookPath -Algorithm SHA256).Hash.ToLowerInvariant()) $expectedWorkbookHash 'Workbook SHA-256 changed'
$sheets = @(Get-WorkbookInventory $WorkbookPath)
Assert-Equal $sheets.Count 1 'Workbook worksheet count'
Assert-Equal $sheets[0].Name 'Sheet1' 'Workbook sheet name'
Assert-Equal $sheets[0].State 'visible' 'Workbook sheet visibility'
Assert-Equal $sheets[0].UsedRange 'A1:C47' 'Workbook used range'
Assert-Equal $sheets[0].HiddenRows 0 'Workbook hidden-row count'
Assert-Equal $sheets[0].HiddenColumns 0 'Workbook hidden-column count'
Assert-Equal $sheets[0].MergedCells 0 'Workbook merged-cell count'
Assert-Equal $sheets[0].FormulaCells 0 'Workbook formula-cell count'
Assert-Equal $sheets[0].Rows.Count 47 'Workbook physical row count'
Assert-Equal $sheets[0].Rows[0].A 'SITE GROUP' 'Workbook column A header'
Assert-Equal $sheets[0].Rows[0].B 'SITE' 'Workbook column B header'
Assert-Equal $sheets[0].Rows[0].C 'CITY' 'Workbook column C header'
$workbookRows = @($sheets[0].Rows | Where-Object { $_.RowNumber -ge 2 -and -not [string]::IsNullOrWhiteSpace($_.A + $_.B + $_.C) })
Assert-Equal $workbookRows.Count 46 'Workbook Site count'
$normalizedGroups = @($workbookRows | Group-Object { Normalize-WorkbookValue $_.A })
Assert-Equal $normalizedGroups.Count 39 'Workbook normalized Site Group count'

$groupHeaders = @('site_group_id','site_group_code','site_group_name','original_site_group_name','business_label','operator_entity_name','timezone_name','default_currency_code','source_workbook','source_sheet','source_rows','site_count','identity_review_status','proposed_site_group_status','public_lookup_enabled','default_payment_enabled','notes')
$siteHeaders = @('site_id','site_code','site_name','original_site_name','site_group_id','site_group_code','source_workbook','source_sheet','source_row','original_location_label','jurisdiction_id','jurisdiction_code','jurisdiction_display_name','psgc_code','region_code','province_code','municipality_or_city_type','location_evidence_status','location_source_url','location_source_reference','hikcentral_activation_candidacy','proposed_parking_lot_index_code','identity_review_status','proposed_site_status','public_lookup_enabled','payment_enabled','timezone_name','currency_code','notes')
$assignmentHeaders = @('site_jurisdiction_assignment_id','site_id','site_code','jurisdiction_id','jurisdiction_code','proposed_assignment_status','proposed_effective_from','effective_date_approval_status','source_reference','evidence_status','notes')
$coverageHeaders = @('jurisdiction_id','jurisdiction_code','jurisdiction_display_name','entitlement_type','parking_policy_identified','benefit_type','free_period_minutes','discount_percent','residency_scope','documentary_requirements_summary','policy_effective_from','policy_effective_to','ordinance_or_authority_reference','ordinance_number_status','primary_source_url','secondary_source_url','repository_source_reference','source_quality_classification','operational_verification_status','legal_review_status','proposed_seed_eligibility','proposed_runtime_publication_eligibility','manual_review_required','conflict_summary','notes')
$sourceHeaders = @('source_id','source_type','title','filename_or_url','repository_path','sha256','publisher','publication_date','access_date','primary_or_secondary','authority_classification','scope','limitations','rows_or_decisions_supported')

$groups = Import-ManifestCsv $groupPath $groupHeaders
$sites = Import-ManifestCsv $sitePath $siteHeaders
$assignments = Import-ManifestCsv $assignmentPath $assignmentHeaders
$coverage = Import-ManifestCsv $coveragePath $coverageHeaders
$sources = Import-ManifestCsv $sourcePath $sourceHeaders
Assert-Equal $groups.Count 39 'Site Group manifest count'
Assert-Equal $sites.Count 46 'Site manifest count'
Assert-Equal $assignments.Count 46 'Assignment manifest count'
Assert-Equal $coverage.Count 26 'Coverage manifest count'

$uuidNamespace = [Guid]'6ba7b811-9dad-11d1-80b4-00c04fd430c8'
$allProposedIds = New-Object Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
foreach ($g in $groups) {
    Assert-True ($g.site_group_code.Length -le 64 -and $g.site_group_code -cmatch '^[A-Z0-9]+(?:-[A-Z0-9]+)*$') "Invalid Site Group code '$($g.site_group_code)'"
    Assert-Equal $g.site_group_id (New-UuidV5 $uuidNamespace "https://exitpass.ph/v1.3/carparks/site-groups/$($g.site_group_code)") "Site Group UUIDv5 mismatch for $($g.site_group_code)"
    Assert-True ($allProposedIds.Add($g.site_group_id)) "Duplicate proposed UUID $($g.site_group_id)"
    Assert-Equal $g.source_workbook 'Carparks.xlsx' "Site Group workbook filename for $($g.site_group_code)"
    Assert-Equal $g.proposed_site_group_status 'DRAFT' "Site Group status for $($g.site_group_code)"
    Assert-Equal $g.public_lookup_enabled 'FALSE' "Site Group public lookup for $($g.site_group_code)"
    Assert-Equal $g.default_payment_enabled 'FALSE' "Site Group payment posture for $($g.site_group_code)"
}
Assert-Equal (@($groups.site_group_code | Sort-Object -Unique).Count) $groups.Count 'Unique Site Group codes'
Assert-Equal (@($groups.site_group_id | Sort-Object -Unique).Count) $groups.Count 'Unique Site Group IDs'

$canonical = @{
 'PARANAQUE'=@('f7a1b4b9-17a9-89de-5059-f72779616f23','1381000000','NCR','City of Parañaque'); 'QUEZON_CITY'=@('79893901-65d3-7c29-0099-25e937a7c8c9','1381300000','NCR','Quezon City');
 'MALABON'=@('46a4b330-a065-daad-5de5-16654b67164f','1380400000','NCR','City of Malabon'); 'PASIG'=@('20650612-8f91-3b4a-bba8-5d8afe29ef5a','1381200000','NCR','City of Pasig');
 'TAGUIG'=@('c7514a40-c898-f3a2-0bfa-530b26daa273','1381500000','NCR','City of Taguig'); 'MAKATI'=@('557b0a76-8ffe-0818-d342-5b86dba06705','1380300000','NCR','City of Makati');
 'SAN_JUAN'=@('d20727ab-9024-d233-ab75-3d49245b452c','1381400000','NCR','City of San Juan'); 'MANDALUYONG'=@('20dcf68a-511f-8208-7dfa-1688425d4d66','1380500000','NCR','City of Mandaluyong');
 'MUNTINLUPA'=@('d7ef112d-06ee-b57c-34b6-fae8c457a0c6','1380800000','NCR','City of Muntinlupa'); 'MANILA'=@('e5959354-04af-4540-9889-6e040b6cd399','1380600000','NCR','City of Manila');
 'LAPU_LAPU'=@('23104fc9-a144-381c-4347-ccb2aa1a2998','0731100000','REGION_VII','City of Lapu-Lapu'); 'CEBU_CITY'=@('42689eb0-66a8-04bb-96fd-c8d32caad475','0730600000','REGION_VII','City of Cebu');
 'DAVAO_CITY'=@('2ebef844-416b-c827-357c-742d2c8d56aa','1130700000','REGION_XI','Davao City')
}

$groupByCode = @{}; foreach ($g in $groups) { $groupByCode[$g.site_group_code] = $g }
$siteByCode = @{}
foreach ($s in $sites) {
    Assert-True ($s.site_code.Length -le 64 -and $s.site_code -cmatch '^[A-Z0-9]+(?:-[A-Z0-9]+)*$') "Invalid Site code '$($s.site_code)'"
    Assert-Equal $s.site_id (New-UuidV5 $uuidNamespace "https://exitpass.ph/v1.3/carparks/sites/$($s.site_code)") "Site UUIDv5 mismatch for $($s.site_code)"
    Assert-True ($allProposedIds.Add($s.site_id)) "Duplicate proposed UUID $($s.site_id)"
    Assert-True $groupByCode.ContainsKey($s.site_group_code) "Site '$($s.site_code)' references missing Site Group"
    Assert-Equal $s.site_group_id $groupByCode[$s.site_group_code].site_group_id "Site Group ID mismatch for $($s.site_code)"
    Assert-True $canonical.ContainsKey($s.jurisdiction_code) "Unknown canonical jurisdiction '$($s.jurisdiction_code)'"
    $j = $canonical[$s.jurisdiction_code]
    Assert-Equal $s.jurisdiction_id $j[0] "Canonical jurisdiction ID for $($s.site_code)"
    Assert-Equal $s.psgc_code $j[1] "Canonical PSGC for $($s.site_code)"
    Assert-Equal $s.region_code $j[2] "Canonical region for $($s.site_code)"
    Assert-Equal $s.jurisdiction_display_name $j[3] "Canonical jurisdiction name for $($s.site_code)"
    Assert-True ($s.psgc_code -cmatch '^\d{10}$') "PSGC must contain exactly 10 digits for $($s.site_code)"
    if ($s.region_code -eq 'NCR') { Assert-Equal $s.province_code '' "NCR must not be represented as a province for $($s.site_code)" }
    Assert-True ($s.jurisdiction_code -notin @('CEBU','DAVAO','METRO_MANILA','NCR','RIZAL','LAGUNA','BULACAN')) "Broad geographic label used as final jurisdiction"
    Assert-True (-not [string]::IsNullOrWhiteSpace($s.location_source_reference)) "Site '$($s.site_code)' lacks location provenance"
    Assert-True (-not [string]::IsNullOrWhiteSpace($s.location_source_url)) "Site '$($s.site_code)' lacks a researched source URL"
    Assert-Equal $s.source_workbook 'Carparks.xlsx' "Site workbook filename for $($s.site_code)"
    Assert-Equal $s.proposed_site_status 'DRAFT' "Site status for $($s.site_code)"
    Assert-Equal $s.public_lookup_enabled 'FALSE' "Site public lookup for $($s.site_code)"
    Assert-Equal $s.payment_enabled 'FALSE' "Site payment posture for $($s.site_code)"
    $siteByCode[$s.site_code] = $s
}
Assert-Equal (@($sites.site_code | Sort-Object -Unique).Count) $sites.Count 'Unique Site codes'
Assert-Equal (@($sites.site_id | Sort-Object -Unique).Count) $sites.Count 'Unique Site IDs'

foreach ($w in $workbookRows) {
    $manifestSite = @($sites | Where-Object { [int]$_.source_row -eq $w.RowNumber })
    Assert-Equal $manifestSite.Count 1 "Exactly one Site manifest row for workbook row $($w.RowNumber)"
    Assert-Equal $manifestSite[0].original_site_name $w.B "Original Site at workbook row $($w.RowNumber)"
    Assert-Equal $manifestSite[0].original_location_label $w.C "Original location at workbook row $($w.RowNumber)"
    Assert-Equal $manifestSite[0].site_group_code (Get-CatalogCode $w.A) "Normalized Site Group code at workbook row $($w.RowNumber)"
}
foreach ($wg in $normalizedGroups) {
    $code = Get-CatalogCode $wg.Name
    $manifestGroup = @($groups | Where-Object { $_.site_group_code -eq $code })
    Assert-Equal $manifestGroup.Count 1 "Exactly one Site Group manifest row for '$($wg.Name)'"
    Assert-Equal $manifestGroup[0].site_count $wg.Count "Site count for Site Group '$code'"
    $expectedRows = (@($wg.Group | ForEach-Object { $_.RowNumber }) -join ';')
    Assert-Equal $manifestGroup[0].source_rows $expectedRows "Source rows for Site Group '$code'"
}

$assignmentBySite = @{}
foreach ($a in $assignments) {
    Assert-True $siteByCode.ContainsKey($a.site_code) "Assignment references missing Site '$($a.site_code)'"
    $s = $siteByCode[$a.site_code]
    Assert-Equal $a.site_id $s.site_id "Assignment Site ID for $($a.site_code)"
    Assert-Equal $a.jurisdiction_id $s.jurisdiction_id "Assignment jurisdiction ID for $($a.site_code)"
    Assert-Equal $a.jurisdiction_code $s.jurisdiction_code "Assignment jurisdiction code for $($a.site_code)"
    Assert-Equal $a.site_jurisdiction_assignment_id (New-UuidV5 $uuidNamespace "https://exitpass.ph/v1.3/carparks/site-jurisdiction-assignments/$($a.site_code)/$($a.jurisdiction_code)") "Assignment UUIDv5 for $($a.site_code)"
    Assert-True ($allProposedIds.Add($a.site_jurisdiction_assignment_id)) "Duplicate proposed UUID $($a.site_jurisdiction_assignment_id)"
    Assert-Equal $a.proposed_assignment_status 'PENDING_APPROVAL' "Assignment status for $($a.site_code)"
    Assert-Equal $a.proposed_effective_from '' "No effective date may be invented for $($a.site_code)"
    Assert-Equal $a.effective_date_approval_status 'OPERATOR_APPROVAL_REQUIRED' "Effective-date approval for $($a.site_code)"
    Assert-True (-not $assignmentBySite.ContainsKey($a.site_code)) "Multiple current proposed assignments for $($a.site_code)"
    $assignmentBySite[$a.site_code] = $a
}
Assert-Equal $assignmentBySite.Count $sites.Count 'Every Site has exactly one proposed assignment'

foreach ($expected in @(@('PITX',2),@('BRIDGETOWNE',2),@('MACTAN-NEW-TOWN',6))) {
    Assert-Equal (@($sites | Where-Object { $_.site_group_code -eq $expected[0] }).Count) $expected[1] "Multi-Site topology count for $($expected[0])"
}
$mactan = @($sites | Where-Object { $_.site_group_code -eq 'MACTAN-NEW-TOWN' })
Assert-Equal $mactan.Count 6 'Mactan New Town Site count'
foreach ($s in $mactan) {
    Assert-Equal $s.jurisdiction_id '23104fc9-a144-381c-4347-ccb2aa1a2998' "Mactan Lapu-Lapu UUID for $($s.site_code)"
    Assert-Equal $s.jurisdiction_code 'LAPU_LAPU' "Mactan jurisdiction for $($s.site_code)"
    Assert-Equal $s.psgc_code '0731100000' "Mactan PSGC for $($s.site_code)"
    Assert-Equal $assignmentBySite[$s.site_code].jurisdiction_id '23104fc9-a144-381c-4347-ccb2aa1a2998' "Mactan assignment UUID for $($s.site_code)"
}
$bridgetowne = @($sites | Where-Object { $_.site_group_code -eq 'BRIDGETOWNE' })
Assert-Equal $bridgetowne.Count 2 'Bridgetowne Site count'
Assert-Equal (($bridgetowne.source_row | Sort-Object) -join ';') '36;37' 'Bridgetowne source rows'
foreach ($s in $bridgetowne) {
    Assert-Equal $s.jurisdiction_code 'PASIG' "Bridgetowne row-level jurisdiction for $($s.site_code)"
    Assert-True ($s.location_source_url -match 'ncr\.emb\.gov\.ph' -and $s.location_source_url -match 'pasigcity\.gov\.ph') "Bridgetowne row-level official evidence is incomplete for $($s.site_code)"
}

$representedJurisdictions = @($sites.jurisdiction_code | Sort-Object -Unique)
Assert-Equal $representedJurisdictions.Count 13 'Represented jurisdiction count'
foreach ($code in $representedJurisdictions) {
    $rows = @($coverage | Where-Object { $_.jurisdiction_code -eq $code })
    Assert-Equal $rows.Count 2 "Coverage row count for $code"
    Assert-Equal (($rows.entitlement_type | Sort-Object) -join ';') 'PWD;SENIOR_CITIZEN' "Separate Senior Citizen and PWD analysis for $code"
    foreach ($row in $rows) {
        Assert-True (-not [string]::IsNullOrWhiteSpace($row.repository_source_reference) -or -not [string]::IsNullOrWhiteSpace($row.primary_source_url) -or -not [string]::IsNullOrWhiteSpace($row.secondary_source_url)) "Coverage row lacks source provenance: $code/$($row.entitlement_type)"
        Assert-Equal $row.proposed_runtime_publication_eligibility 'NOT_ELIGIBLE' "Runtime-publication posture for $code/$($row.entitlement_type)"
        if ($row.parking_policy_identified -eq 'FALSE') {
            Assert-Equal $row.benefit_type '' "Unknown benefit type must remain blank for $code/$($row.entitlement_type)"
            Assert-Equal $row.free_period_minutes '' "Unknown free period must remain blank for $code/$($row.entitlement_type)"
            Assert-Equal $row.discount_percent '' "Unknown discount must remain blank for $code/$($row.entitlement_type)"
            Assert-Equal $row.policy_effective_from '' "Unknown effective date must remain blank for $code/$($row.entitlement_type)"
        }
    }
}
$paranaqueSenior = @($coverage | Where-Object { $_.jurisdiction_code -eq 'PARANAQUE' -and $_.entitlement_type -eq 'SENIOR_CITIZEN' })[0]
Assert-Equal $paranaqueSenior.source_quality_classification 'OPERATIONALLY_VERIFIED_OFFICIAL_TEXT_UNAVAILABLE' 'Parañaque Senior Citizen source distinction'
Assert-Equal $paranaqueSenior.operational_verification_status 'VERIFIED_ACTIVE_OPERATIONAL' 'Parañaque Senior Citizen operational verification'
Assert-Equal $paranaqueSenior.ordinance_or_authority_reference '' 'Unknown Parañaque Senior Citizen ordinance number must remain blank'
Assert-Equal $paranaqueSenior.manual_review_required 'TRUE' 'Parañaque Senior Citizen manual review'

$candidate = @($sites | Where-Object { $_.hikcentral_activation_candidacy -eq 'PROPOSED_NOT_ACTIVATED' })
Assert-Equal $candidate.Count 1 'Exactly one proposed HikCentral candidate'
Assert-Equal $candidate[0].site_name 'PITX Level 3' 'HikCentral candidate identity'
Assert-Equal $candidate[0].proposed_parking_lot_index_code '1' 'HikCentral proposed parking lot index'

$workbookSource = @($sources | Where-Object { $_.source_id -eq 'SRC-WORKBOOK-CARPARKS' })
Assert-Equal $workbookSource.Count 1 'Workbook provenance row count'
Assert-Equal $workbookSource[0].filename_or_url 'Carparks.xlsx' 'Workbook source filename'
Assert-Equal $workbookSource[0].sha256 $expectedWorkbookHash 'Workbook provenance SHA-256'
$canonicalSource = @($sources | Where-Object { $_.source_id -eq 'SRC-CANONICAL-DB' })[0]
Assert-True ($canonicalSource.repository_path -match '1e307c2bd56c2738a92cdd87571f6caeeaf07b3d') 'Source register must cite the merged canonical database commit'
foreach ($row in @($groups) + @($sites)) { Assert-Equal $row.source_workbook 'Carparks.xlsx' 'Consistent workbook source filename' }

$dataText = [IO.File]::ReadAllText($groupPath) + [IO.File]::ReadAllText($sitePath) + [IO.File]::ReadAllText($assignmentPath) + [IO.File]::ReadAllText($coveragePath) + [IO.File]::ReadAllText($sourcePath)
Assert-True ($dataText -notmatch 'List of Car Parks\(1\)\.xlsx') 'Substitute workbook name is prohibited'
Assert-True ($dataText -notmatch '0730110000') 'Retired Lapu-Lapu PSGC is prohibited in proposed data'
Assert-True ($dataText -notmatch '77000000-0000-0000-0000-000000000001|77000000-0000-0000-0000-000000000002') 'Colliding fixture UUID is prohibited in proposed catalog data'
Assert-True ($dataText -notmatch '(?i)(password\s*=|appsecret\s*=|connectionstrings__|postgres(?:ql)?://|jdbc:|x-ca-signature\s*=|bearer\s+[a-z0-9._-]+)') 'Secret, credential, or connection-string material is prohibited'

$mainText = [IO.File]::ReadAllText($mainDoc)
Assert-True ($mainText -match 'identity-preserving correction') 'Documentation must explain resolution of the prior Lapu-Lapu blocker'
Assert-True ($mainText -match 'not recalculated when an external PSGC changes') 'Documentation must preserve internal identity across external-code correction'
Assert-True ($mainText -match '39 Site Groups' -and $mainText -match '46 Sites') 'Documentation must state workbook-derived counts'
Assert-True ($mainText -match '46 - 5 = 41' -and $mainText -match 'Bridgetowne') 'Documentation must resolve the 39-versus-41 discrepancy'

$statusLines = @(& git -C $RepositoryRoot status --porcelain=v1 --untracked-files=all)
Assert-True ($LASTEXITCODE -eq 0) 'Git status failed'
$changedPaths = @($statusLines | ForEach-Object { if ($_.Length -ge 4) { $_.Substring(3).Trim('"') } })
$allowedPrefixes = @('docs/v1.3/central-pms/seed-manifests/','scripts/v1.3/catalog/Test-RealisticCarparkCatalogSeedManifest.ps1')
foreach ($path in $changedPaths) {
    $normalized = $path.Replace('\','/')
    Assert-True ($normalized.StartsWith($allowedPrefixes[0]) -or $normalized -eq $allowedPrefixes[1]) "Out-of-scope changed path '$normalized'"
    Assert-True (-not $normalized.EndsWith('.sql', [StringComparison]::OrdinalIgnoreCase)) "SQL change is prohibited: $normalized"
    Assert-True (-not $normalized.EndsWith('.xlsx', [StringComparison]::OrdinalIgnoreCase)) "Workbook addition is prohibited: $normalized"
}
Assert-True (-not (Test-Path -LiteralPath (Join-Path $RepositoryRoot 'Carparks.xlsx'))) 'Carparks.xlsx must remain outside the worktree'
$newXlsx = @($statusLines | Where-Object { $_ -match '(?i)\.xlsx$' })
Assert-Equal $newXlsx.Count 0 'No source or substitute workbook may be added'

$trackedGuids = New-Object Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
$gitGuids = @(& git -C $RepositoryRoot grep -I -h -o -E '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[1-5][0-9a-fA-F]{3}-[89abAB][0-9a-fA-F]{3}-[0-9a-fA-F]{12}' HEAD -- . 2>$null)
foreach ($id in $gitGuids) { [void]$trackedGuids.Add($id) }
foreach ($id in $allProposedIds) { Assert-True (-not $trackedGuids.Contains($id)) "Proposed UUID collides with a tracked canonical or fixture identity: $id" }

Write-Output "PASS: realistic carpark catalog manifest validation completed ($script:Checks checks)."
Write-Output "Workbook: Carparks.xlsx; SHA-256: $expectedWorkbookHash"
Write-Output "Counts: 39 Site Groups; 46 Sites; 46 assignments; 26 policy rows; 13 jurisdictions."
Write-Output "Mactan New Town: 6/6 -> LAPU_LAPU / 0731100000 / 23104fc9-a144-381c-4347-ccb2aa1a2998."
Write-Output "Bridgetowne: rows 36 and 37 -> PASIG with row-level evidence."
Write-Output "HikCentral candidate: PITX Level 3 / PROPOSED_NOT_ACTIVATED / parking lot index 1."
exit 0
