namespace ExitPass.CentralPms.Application.ManagementPlatform;

public sealed class SalesInvoiceProfileAdministrationService : ISalesInvoiceProfileAdministrationService
{
    private readonly IPosServerSalesInvoiceProfileAdminClient _client;

    public SalesInvoiceProfileAdministrationService(IPosServerSalesInvoiceProfileAdminClient client)
    {
        _client = client;
    }

    public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformFiscalIdentity>> CreateFiscalIdentityAsync(
        ManagementPlatformFiscalIdentityMutationRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken)
    {
        var invalid = ValidateFiscalIdentityRequest(request, context);
        return invalid is not null
            ? Task.FromResult(invalid)
            : _client.CreateFiscalIdentityAsync(request, context, cancellationToken);
    }

    public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformFiscalIdentity>> GetFiscalIdentityAsync(
        Guid fiscalIdentityId,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken) =>
        fiscalIdentityId == Guid.Empty
            ? Task.FromResult(Invalid<ManagementPlatformFiscalIdentity>(context, "fiscal_identity_id_required"))
            : _client.GetFiscalIdentityAsync(fiscalIdentityId, context, cancellationToken);

    public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformFiscalIdentity>> UpdateFiscalIdentityAsync(
        Guid fiscalIdentityId,
        ManagementPlatformFiscalIdentityMutationRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken)
    {
        if (fiscalIdentityId == Guid.Empty)
        {
            return Task.FromResult(Invalid<ManagementPlatformFiscalIdentity>(context, "fiscal_identity_id_required"));
        }

        var invalid = ValidateFiscalIdentityRequest(request, context);
        return invalid is not null
            ? Task.FromResult(invalid)
            : _client.UpdateFiscalIdentityAsync(fiscalIdentityId, request, context, cancellationToken);
    }

    public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> CreateProfileAsync(
        ManagementPlatformSalesInvoiceHeaderProfileMutationRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken)
    {
        var invalid = ValidateProfileRequest(request, context);
        return invalid is not null
            ? Task.FromResult(invalid)
            : _client.CreateProfileAsync(request, context, cancellationToken);
    }

    public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> GetProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken) =>
        salesInvoiceHeaderProfileId == Guid.Empty
            ? Task.FromResult(Invalid<ManagementPlatformSalesInvoiceHeaderProfile>(context, "sales_invoice_header_profile_id_required"))
            : _client.GetProfileAsync(salesInvoiceHeaderProfileId, context, cancellationToken);

    public Task<PosServerSalesInvoiceProfileAdminResult<IReadOnlyList<ManagementPlatformSalesInvoiceHeaderProfile>>> ListProfilesAsync(
        ManagementPlatformSalesInvoiceHeaderProfileListRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _client.ListProfilesAsync(request, context, cancellationToken);
    }

    public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> UpdateDraftProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformSalesInvoiceHeaderProfileMutationRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken)
    {
        if (salesInvoiceHeaderProfileId == Guid.Empty)
        {
            return Task.FromResult(Invalid<ManagementPlatformSalesInvoiceHeaderProfile>(context, "sales_invoice_header_profile_id_required"));
        }

        var invalid = ValidateProfileRequest(request, context);
        return invalid is not null
            ? Task.FromResult(invalid)
            : _client.UpdateDraftProfileAsync(salesInvoiceHeaderProfileId, request, context, cancellationToken);
    }

    public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileValidation>> ValidateProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken) =>
        salesInvoiceHeaderProfileId == Guid.Empty
            ? Task.FromResult(Invalid<ManagementPlatformSalesInvoiceHeaderProfileValidation>(context, "sales_invoice_header_profile_id_required"))
            : _client.ValidateProfileAsync(salesInvoiceHeaderProfileId, context, cancellationToken);

    public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> ApproveProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformSalesInvoiceHeaderProfileApprovalRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (salesInvoiceHeaderProfileId == Guid.Empty)
        {
            return Task.FromResult(Invalid<ManagementPlatformSalesInvoiceHeaderProfile>(context, "sales_invoice_header_profile_id_required"));
        }

        return string.IsNullOrWhiteSpace(request.ApprovedByRef)
            ? Task.FromResult(Invalid<ManagementPlatformSalesInvoiceHeaderProfile>(context, "approved_by_ref_required"))
            : _client.ApproveProfileAsync(salesInvoiceHeaderProfileId, request, context, cancellationToken);
    }

    public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>> RetireProfileAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformSalesInvoiceHeaderProfileRetirementRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (salesInvoiceHeaderProfileId == Guid.Empty)
        {
            return Task.FromResult(Invalid<ManagementPlatformSalesInvoiceHeaderProfile>(context, "sales_invoice_header_profile_id_required"));
        }

        return string.IsNullOrWhiteSpace(request.RetiredByRef)
            ? Task.FromResult(Invalid<ManagementPlatformSalesInvoiceHeaderProfile>(context, "retired_by_ref_required"))
            : _client.RetireProfileAsync(salesInvoiceHeaderProfileId, request, context, cancellationToken);
    }

    public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileReadiness>> GetEffectiveReadinessAsync(
        ManagementPlatformSalesInvoiceHeaderProfileReadinessRequest request,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.SiteId == Guid.Empty || request.SitePosServerId == Guid.Empty
            ? Task.FromResult(Invalid<ManagementPlatformSalesInvoiceHeaderProfileReadiness>(context, "site_and_site_pos_server_required"))
            : _client.GetEffectiveReadinessAsync(request, context, cancellationToken);
    }

    public Task<PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfileUsage>> GetProfileUsageAsync(
        Guid salesInvoiceHeaderProfileId,
        ManagementPlatformPosServerAdminRequestContext context,
        CancellationToken cancellationToken) =>
        salesInvoiceHeaderProfileId == Guid.Empty
            ? Task.FromResult(Invalid<ManagementPlatformSalesInvoiceHeaderProfileUsage>(context, "sales_invoice_header_profile_id_required"))
            : _client.GetProfileUsageAsync(salesInvoiceHeaderProfileId, context, cancellationToken);

    private static PosServerSalesInvoiceProfileAdminResult<ManagementPlatformFiscalIdentity>? ValidateFiscalIdentityRequest(
        ManagementPlatformFiscalIdentityMutationRequest request,
        ManagementPlatformPosServerAdminRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(request);
        return string.IsNullOrWhiteSpace(request.RegisteredBusinessName) ||
            string.IsNullOrWhiteSpace(request.RegisteredBusinessAddress) ||
            string.IsNullOrWhiteSpace(request.Tin) ||
            string.IsNullOrWhiteSpace(request.TaxpayerPosture) ||
            string.IsNullOrWhiteSpace(request.RequestedByRef)
            ? Invalid<ManagementPlatformFiscalIdentity>(context, "fiscal_identity_request_shape_invalid")
            : null;
    }

    private static PosServerSalesInvoiceProfileAdminResult<ManagementPlatformSalesInvoiceHeaderProfile>? ValidateProfileRequest(
        ManagementPlatformSalesInvoiceHeaderProfileMutationRequest request,
        ManagementPlatformPosServerAdminRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.FiscalIdentityId == Guid.Empty ||
            request.SiteId == Guid.Empty ||
            request.SitePosServerId == Guid.Empty ||
            request.ProfileVersion <= 0 ||
            string.IsNullOrWhiteSpace(request.TemplateVersion) ||
            string.IsNullOrWhiteSpace(request.PresentationVersion) ||
            string.IsNullOrWhiteSpace(request.PosSerialNumber) ||
            string.IsNullOrWhiteSpace(request.MachineIdentificationNumber) ||
            string.IsNullOrWhiteSpace(request.ParkingLocationDisplay) ||
            string.IsNullOrWhiteSpace(request.SupplierDeveloperRegisteredName) ||
            string.IsNullOrWhiteSpace(request.SupplierDeveloperAddress) ||
            string.IsNullOrWhiteSpace(request.SupplierDeveloperTin) ||
            string.IsNullOrWhiteSpace(request.BirAccreditationNumber) ||
            string.IsNullOrWhiteSpace(request.PtuNumber) ||
            string.IsNullOrWhiteSpace(request.SalesInvoiceLegalStatement) ||
            string.IsNullOrWhiteSpace(request.RequestedByRef)
            ? Invalid<ManagementPlatformSalesInvoiceHeaderProfile>(context, "sales_invoice_header_profile_request_shape_invalid")
            : null;
    }

    private static PosServerSalesInvoiceProfileAdminResult<T> Invalid<T>(
        ManagementPlatformPosServerAdminRequestContext context,
        string code) =>
        PosServerSalesInvoiceProfileAdminResult<T>.Failure(
            PosServerSalesInvoiceProfileAdminOutcome.InvalidRequest,
            code,
            "Management Platform request shape is invalid.",
            context.GetOrCreateCorrelationId());
}
