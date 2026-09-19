using System.Net.Mail;
using System.Security.Cryptography;
using ExitPass.CentralPms.Application.HumanAuthentication;
using Microsoft.Extensions.Options;

namespace ExitPass.CentralPms.Application.ManagementPlatform;

public sealed class ManagementPlatformIdentityAdministrationService : IManagementPlatformIdentityAdministrationService
{
    private static readonly HashSet<string> SupportedUserTypes = new(StringComparer.Ordinal)
    {
        "INTERNAL_ADMIN",
        "OPERATIONS_USER",
        "SITE_OPERATOR",
        "SUPPORT_USER",
        "FINANCE_USER",
        "COMPLIANCE_USER",
        "MERCHANT_USER",
        "SECURITY_USER",
        "OTHER"
    };

    private readonly IManagementPlatformIdentityAdministrationRepository _repository;
    private readonly IHumanAuthenticationAdministrationGateway _authenticationGateway;
    private readonly HumanAuthenticationOptions _authenticationOptions;
    private readonly TimeProvider _timeProvider;
    private readonly IHumanPasswordHasher? _passwordHasher;
    private readonly ITotpProvider? _totpProvider;
    private readonly ITotpSecretProtector? _totpProtector;

    public ManagementPlatformIdentityAdministrationService(
        IManagementPlatformIdentityAdministrationRepository repository,
        IHumanAuthenticationAdministrationGateway authenticationGateway,
        IOptions<HumanAuthenticationOptions> authenticationOptions,
        TimeProvider timeProvider,
        IHumanPasswordHasher? passwordHasher = null,
        ITotpProvider? totpProvider = null,
        ITotpSecretProtector? totpProtector = null)
    {
        _repository = repository;
        _authenticationGateway = authenticationGateway;
        _authenticationOptions = authenticationOptions.Value;
        _timeProvider = timeProvider;
        _passwordHasher = passwordHasher;
        _totpProvider = totpProvider;
        _totpProtector = totpProtector;
    }

    public ManagementPlatformIdentityAdministrationService(
        IManagementPlatformIdentityAdministrationRepository repository,
        IHumanAuthenticationAdministrationGateway authenticationGateway)
        : this(repository, authenticationGateway, Options.Create(new HumanAuthenticationOptions()), TimeProvider.System)
    {
    }

    public Task<IdentityAdministrationResult<IReadOnlyList<IdentityUserSummary>>> ListUsersAsync(
        IdentityAdministrationActor actor, IdentityUserSearch search, Guid correlationId, CancellationToken cancellationToken) =>
        _repository.ListUsersAsync(actor, search with { Offset = Math.Max(0, search.Offset), Limit = Math.Clamp(search.Limit, 1, 200) }, correlationId, cancellationToken);

    public Task<IdentityAdministrationResult<IdentityUserDetail>> GetUserAsync(
        IdentityAdministrationActor actor, Guid userReference, Guid correlationId, CancellationToken cancellationToken) =>
        _repository.GetUserAsync(actor, RequireReference(userReference), correlationId, cancellationToken);

    public Task<IdentityAdministrationResult<IdentityUserSummary>> CreateUserAsync(
        IdentityAdministrationActor actor, CreateIdentityUserCommand command, CancellationToken cancellationToken) =>
        _repository.CreateUserAsync(actor, NormalizeCreateCommand(command), cancellationToken);

    public async Task<IdentityAdministrationResult<CreateIdentityUserResult>> CreateInvitedUserAsync(
        IdentityAdministrationActor actor, CreateIdentityUserCommand command, CancellationToken cancellationToken)
    {
        var normalized = NormalizeCreateCommand(command);
        if (_passwordHasher is null || _totpProvider is null || _totpProtector is null || !_totpProtector.IsConfigured)
        {
            return IdentityAdministrationResult<CreateIdentityUserResult>.Failed(
                IdentityAdministrationOutcome.IntegrationUnavailable, "HUMAN_BOOTSTRAP_CRYPTOGRAPHY_UNAVAILABLE",
                "Human account provisioning is unavailable.", normalized.CorrelationId);
        }

        var now = _timeProvider.GetUtcNow();
        var userReference = Guid.NewGuid();
        var authenticatorReference = Guid.NewGuid();
        var temporaryPassword = GenerateTemporaryPassword();
        var passwordHash = await _passwordHasher.HashAsync(temporaryPassword, cancellationToken);
        var totpSecret = _totpProvider.GenerateSecret();
        var totpSharedSecret = _totpProvider.EncodeSecret(totpSecret);
        var provisioningUri = _totpProvider.BuildProvisioningUri(normalized.Username, totpSecret);
        var protectedSecret = _totpProtector.Protect(userReference, authenticatorReference, totpSecret);
        CryptographicOperations.ZeroMemory(totpSecret);
        var expiresAt = now.AddHours(HumanAuthenticationOptions.RequiredTemporaryPasswordHours);
        normalized = normalized with
        {
            Bootstrap = new HumanBootstrapPersistenceMaterial(userReference, Guid.NewGuid(), passwordHash, expiresAt,
                authenticatorReference, protectedSecret, _totpProtector.KeyReference, _totpProtector.KeyVersion,
                _totpProtector.EnvelopeFormatVersion)
        };

        var created = await _repository.CreateUserAsync(actor, normalized, cancellationToken);
        if (created.Outcome != IdentityAdministrationOutcome.Success || created.Value is null)
        {
            return Propagate<CreateIdentityUserResult, IdentityUserSummary>(created);
        }

        var invitation = new IdentityInvitationStatus("PASSWORD_CHANGE_REQUIRED", null, null,
            null, now, expiresAt, "ONE_TIME_BOOTSTRAP");
        return IdentityAdministrationResult<CreateIdentityUserResult>.Succeeded(
            new(created.Value, invitation, null,
                new OneTimeHumanBootstrapMaterial(temporaryPassword, expiresAt, totpSharedSecret, provisioningUri)),
            normalized.CorrelationId, "CREATED");
    }

    public Task<IdentityAdministrationResult<IdentityUserSummary>> UpdateUserAsync(
        IdentityAdministrationActor actor, UpdateIdentityUserCommand command, CancellationToken cancellationToken) =>
        _repository.UpdateUserAsync(actor, command with
        {
            UserReference = RequireReference(command.UserReference),
            DisplayName = RequireText(command.DisplayName, 128, nameof(command.DisplayName)),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode))
        }, cancellationToken);

    public Task<IdentityAdministrationResult<IdentityUserSummary>> ChangeUserLifecycleAsync(
        IdentityAdministrationActor actor, ChangeIdentityUserLifecycleCommand command, CancellationToken cancellationToken) =>
        _repository.ChangeUserLifecycleAsync(actor, command with
        {
            UserReference = RequireReference(command.UserReference),
            Transition = RequireCode(command.Transition, nameof(command.Transition)),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode))
        }, cancellationToken);

    public Task<IdentityAdministrationResult<IdentityUserSummary>> CancelInvitationAsync(
        IdentityAdministrationActor actor, CancelIdentityInvitationCommand command, CancellationToken cancellationToken) =>
        _repository.CancelInvitationAsync(actor, command with
        {
            UserReference = RequireReference(command.UserReference),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode))
        }, cancellationToken);

    public async Task<IdentityAdministrationResult<CredentialResetChallengeResult>> ReissueInvitationAsync(
        IdentityAdministrationActor actor, ReissueIdentityInvitationCommand command, CancellationToken cancellationToken)
    {
        var mode = RequireCode(command.ActivationDeliveryMode, nameof(command.ActivationDeliveryMode));
        var reason = RequireCode(command.ReasonCode, nameof(command.ReasonCode));
        var target = await _repository.GetUserAsync(actor, RequireReference(command.UserReference), command.CorrelationId, cancellationToken);
        if (target.Outcome != IdentityAdministrationOutcome.Success || target.Value is null)
        {
            return Propagate<CredentialResetChallengeResult, IdentityUserDetail>(target);
        }
        if (target.Value.User.Status != "INVITED")
        {
            return IdentityAdministrationResult<CredentialResetChallengeResult>.Failed(
                IdentityAdministrationOutcome.Conflict, "INVITATION_NOT_PENDING",
                "Only an invited account can receive a new activation challenge.", command.CorrelationId);
        }
        var validation = ValidateActivationDelivery<CredentialResetChallengeResult>(mode,
            target.Value.User.MaskedEmail, command.AdminIssuedHandoffAcknowledged, command.CorrelationId,
            emailAlreadyValidated: true);
        if (validation is not null) return validation;

        return await IssueCredentialChallengeAsync(actor,
            new(command.UserReference, "ACCOUNT_ACTIVATION",
                _timeProvider.GetUtcNow().AddMinutes(_authenticationOptions.CredentialChallengeMinutes), reason,
                command.CorrelationId, mode, command.AdminIssuedHandoffAcknowledged), cancellationToken);
    }

    public async Task<IdentityAdministrationResult<IReadOnlyList<IdentityRoleDefinition>>> ListRolesAsync(
        IdentityAdministrationActor actor, IdentityRoleCatalogQuery query, Guid correlationId, CancellationToken cancellationToken)
    {
        var result = await _repository.ListRolesAsync(actor, query with { UserType = null }, correlationId, cancellationToken);
        if (result.Outcome != IdentityAdministrationOutcome.Success || result.Value is null) return result;

        var projected = new List<IdentityRoleDefinition>(result.Value.Count);
        foreach (var role in result.Value)
        {
            if (!ApprovedIdentityRoleCatalog.TryGetPolicy(role.Code, out var policy) ||
                policy is null ||
                policy.AllowedApplicationAudiences.Count == 0 ||
                policy.AllowedAssignmentScopes.Count == 0 ||
                (policy.DefaultAssignmentScope is not null &&
                 !policy.AllowedAssignmentScopes.Contains(policy.DefaultAssignmentScope, StringComparer.OrdinalIgnoreCase)))
            {
                return IdentityAdministrationResult<IReadOnlyList<IdentityRoleDefinition>>.Failed(
                    IdentityAdministrationOutcome.IntegrationUnavailable,
                    "IDENTITY_ROLE_POLICY_UNAVAILABLE",
                    "The authoritative role policy is unavailable.",
                    correlationId);
            }

            projected.Add(role with
            {
                ApplicationAccess = policy.AllowedApplicationAudiences.ToArray(),
                ScopePolicy = new IdentityRoleScopePolicy(
                    policy.AllowedAssignmentScopes.ToArray(),
                    AssignmentRequired: true,
                    DefaultScope: policy.DefaultAssignmentScope)
            });
        }

        return result with { Value = projected };
    }

    public Task<IdentityAdministrationResult<IReadOnlyList<IdentityPermissionDefinition>>> ListPermissionsAsync(
        IdentityAdministrationActor actor, Guid correlationId, CancellationToken cancellationToken) =>
        _repository.ListPermissionsAsync(actor, correlationId, cancellationToken);

    public Task<IdentityAdministrationResult<DelegableScopeCatalog>> GetDelegableScopesAsync(
        IdentityAdministrationActor actor, Guid correlationId, CancellationToken cancellationToken) =>
        _repository.GetDelegableScopesAsync(actor, correlationId, cancellationToken);

    public Task<IdentityAdministrationResult<IdentityRoleAssignment>> AssignRoleAsync(
        IdentityAdministrationActor actor, AssignIdentityRoleCommand command, CancellationToken cancellationToken) =>
        _repository.AssignRoleAsync(actor, command with
        {
            UserReference = RequireReference(command.UserReference),
            RoleReference = RequireReference(command.RoleReference),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode)),
            IdempotencyKey = RequireText(command.IdempotencyKey, 128, nameof(command.IdempotencyKey))
        }, cancellationToken);

    public Task<IdentityAdministrationResult<IdentityRoleAssignment>> RevokeRoleAsync(
        IdentityAdministrationActor actor, RevokeIdentityRoleCommand command, CancellationToken cancellationToken) =>
        _repository.RevokeRoleAsync(actor, command with
        {
            UserReference = RequireReference(command.UserReference),
            AssignmentReference = RequireReference(command.AssignmentReference),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode))
        }, cancellationToken);

    public Task<IdentityAdministrationResult<IdentityScopeGrant>> GrantScopeAsync(
        IdentityAdministrationActor actor, GrantIdentityScopeCommand command, CancellationToken cancellationToken) =>
        _repository.GrantScopeAsync(actor, command with
        {
            UserReference = RequireReference(command.UserReference),
            AssignmentReference = RequireReference(command.AssignmentReference),
            ScopeType = RequireCode(command.ScopeType, nameof(command.ScopeType)),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode)),
            IdempotencyKey = RequireText(command.IdempotencyKey, 128, nameof(command.IdempotencyKey))
        }, cancellationToken);

    public Task<IdentityAdministrationResult<IdentityScopeGrant>> RevokeScopeAsync(
        IdentityAdministrationActor actor, RevokeIdentityScopeCommand command, CancellationToken cancellationToken) =>
        _repository.RevokeScopeAsync(actor, command with
        {
            UserReference = RequireReference(command.UserReference),
            AssignmentReference = RequireReference(command.AssignmentReference),
            GrantReference = RequireReference(command.GrantReference),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode))
        }, cancellationToken);

    public async Task<IdentityAdministrationResult<CredentialResetChallengeResult>> IssueCredentialChallengeAsync(
        IdentityAdministrationActor actor, CreateCredentialResetChallengeCommand command, CancellationToken cancellationToken)
    {
        var normalized = command with
        {
            UserReference = RequireReference(command.UserReference),
            Purpose = RequireCode(command.Purpose, nameof(command.Purpose)),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode)),
            DeliveryMode = RequireCode(command.DeliveryMode, nameof(command.DeliveryMode))
        };
        var authorization = await _repository.AuthorizeAuthenticationAdministrationAsync(
            actor, normalized.UserReference, "CREDENTIAL_RESET", normalized.CorrelationId, cancellationToken);
        return authorization.Outcome == IdentityAdministrationOutcome.Success
            ? await _authenticationGateway.IssueCredentialChallengeAsync(actor, normalized, cancellationToken)
            : Propagate<CredentialResetChallengeResult>(authorization);
    }

    public Task<IdentityAdministrationResult<IdentityPrivilegedAccessRequest>> CreatePrivilegedAccessRequestAsync(
        IdentityAdministrationActor actor, CreatePrivilegedAccessRequestCommand command, CancellationToken cancellationToken) =>
        _repository.CreatePrivilegedAccessRequestAsync(actor, command with
        {
            TargetUserReference = RequireReference(command.TargetUserReference),
            RoleReference = RequireReference(command.RoleReference),
            ScopeType = string.IsNullOrWhiteSpace(command.ScopeType) ? null : RequireCode(command.ScopeType, nameof(command.ScopeType)),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode))
        }, cancellationToken);

    public Task<IdentityAdministrationResult<IdentityPrivilegedAccessRequest>> GetPrivilegedAccessRequestAsync(
        IdentityAdministrationActor actor, Guid requestReference, Guid correlationId, CancellationToken cancellationToken) =>
        _repository.GetPrivilegedAccessRequestAsync(actor, RequireReference(requestReference), correlationId, cancellationToken);

    public Task<IdentityAdministrationResult<IdentityPrivilegedAccessRequest>> DecidePrivilegedAccessAsync(
        IdentityAdministrationActor actor, DecidePrivilegedAccessCommand command, CancellationToken cancellationToken) =>
        _repository.DecidePrivilegedAccessAsync(actor, command with
        {
            RequestReference = RequireReference(command.RequestReference),
            Decision = RequireCode(command.Decision, nameof(command.Decision)),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode))
        }, cancellationToken);

    public Task<IdentityAdministrationResult<bool>> ReviewAccessAsync(
        IdentityAdministrationActor actor, ReviewIdentityAccessCommand command, CancellationToken cancellationToken) =>
        _repository.ReviewAccessAsync(actor, command with
        {
            UserReference = RequireReference(command.UserReference),
            Outcome = RequireCode(command.Outcome, nameof(command.Outcome)),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode))
        }, cancellationToken);

    public Task<IdentityAdministrationResult<IReadOnlyList<IdentitySessionSummary>>> ListSessionsAsync(
        IdentityAdministrationActor actor, Guid userReference, Guid correlationId, CancellationToken cancellationToken) =>
        _repository.ListSessionsAsync(actor, RequireReference(userReference), correlationId, cancellationToken);

    public async Task<IdentityAdministrationResult<bool>> RevokeSessionsAsync(
        IdentityAdministrationActor actor, RevokeIdentitySessionCommand command, CancellationToken cancellationToken)
    {
        var normalized = command with
        {
            UserReference = RequireReference(command.UserReference),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode))
        };
        var authorization = await _repository.AuthorizeAuthenticationAdministrationAsync(
            actor, normalized.UserReference, "SESSION_REVOKE", normalized.CorrelationId, cancellationToken);
        return authorization.Outcome == IdentityAdministrationOutcome.Success
            ? await _authenticationGateway.RevokeSessionsAsync(actor, normalized, cancellationToken)
            : Propagate<bool>(authorization);
    }

    public Task<IdentityAdministrationResult<IdentityMfaStatus>> GetMfaStatusAsync(
        IdentityAdministrationActor actor, Guid userReference, Guid correlationId, CancellationToken cancellationToken) =>
        _repository.GetMfaStatusAsync(actor, RequireReference(userReference), correlationId, cancellationToken);

    public Task<IdentityAdministrationResult<bool>> AuthorizeAuthenticationAdministrationAsync(
        IdentityAdministrationActor actor, Guid userReference, string action, Guid correlationId, CancellationToken cancellationToken) =>
        _repository.AuthorizeAuthenticationAdministrationAsync(
            actor, RequireReference(userReference), RequireCode(action, nameof(action)), correlationId, cancellationToken);

    public async Task<IdentityAdministrationResult<IdentityMfaProvisioningResult>> ProvisionMfaAsync(
        IdentityAdministrationActor actor, ProvisionIdentityMfaCommand command, CancellationToken cancellationToken)
    {
        var normalized = command with
        {
            UserReference = RequireReference(command.UserReference),
            Action = RequireCode(command.Action, nameof(command.Action)),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode))
        };
        if (normalized.Action is not ("SETUP" or "RESET") ||
            (normalized.Action == "RESET" && normalized.ExpectedRowVersion is null))
        {
            throw new ArgumentException("The MFA administration action is invalid.", nameof(command));
        }

        var authorization = await _repository.AuthorizeAuthenticationAdministrationAsync(
            actor, normalized.UserReference, "MFA_RESET", normalized.CorrelationId, cancellationToken);
        return authorization.Outcome == IdentityAdministrationOutcome.Success
            ? await _authenticationGateway.ProvisionMfaAsync(actor, normalized, cancellationToken)
            : Propagate<IdentityMfaProvisioningResult>(authorization);
    }

    public async Task<IdentityAdministrationResult<IdentityMfaStatus>> RemoveMfaAsync(
        IdentityAdministrationActor actor, RemoveIdentityMfaCommand command, CancellationToken cancellationToken)
    {
        var normalized = command with
        {
            UserReference = RequireReference(command.UserReference),
            ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode))
        };
        var authorization = await _repository.AuthorizeAuthenticationAdministrationAsync(
            actor, normalized.UserReference, "MFA_REMOVE", normalized.CorrelationId, cancellationToken);
        return authorization.Outcome == IdentityAdministrationOutcome.Success
            ? await _authenticationGateway.RemoveMfaAsync(actor, normalized, cancellationToken)
            : Propagate<IdentityMfaStatus>(authorization);
    }

    public Task<IdentityAdministrationResult<IReadOnlyList<IdentityAuditEntry>>> ListAuditEventsAsync(
        IdentityAdministrationActor actor, Guid userReference, int limit, Guid correlationId, CancellationToken cancellationToken) =>
        _repository.ListAuditEventsAsync(actor, RequireReference(userReference), Math.Clamp(limit, 1, 200), correlationId, cancellationToken);

    private static Guid RequireReference(Guid value) =>
        value != Guid.Empty ? value : throw new ArgumentException("A non-empty reference is required.");

    private static string RequireText(string value, int maximumLength, string parameterName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maximumLength)
        {
            throw new ArgumentException($"{parameterName} is required and must not exceed {maximumLength} characters.", parameterName);
        }

        return normalized;
    }

    private static string RequireCode(string value, string parameterName) =>
        RequireText(value, 64, parameterName).ToUpperInvariant();

    private static string RequireUserType(string value, string parameterName)
    {
        var normalized = RequireCode(value, parameterName);
        if (!SupportedUserTypes.Contains(normalized))
        {
            throw new ArgumentException("The user type is not supported.", parameterName);
        }

        return normalized;
    }

    private CreateIdentityUserCommand NormalizeCreateCommand(CreateIdentityUserCommand command) => command with
    {
        Username = RequireText(command.Username, 128, nameof(command.Username)),
        DisplayName = RequireText(command.DisplayName, 128, nameof(command.DisplayName)),
        Email = NormalizeOptionalEmail(command.Email),
        UserType = RequireUserType(command.UserType, nameof(command.UserType)),
        InitialRoleReference = RequireReference(command.InitialRoleReference),
        InitialScopeType = RequireCode(command.InitialScopeType, nameof(command.InitialScopeType)),
        ReasonCode = RequireCode(command.ReasonCode, nameof(command.ReasonCode)),
        IdempotencyKey = RequireText(command.IdempotencyKey, 128, nameof(command.IdempotencyKey))
    };

    private IdentityAdministrationResult<T>? ValidateActivationDelivery<T>(
        string mode, string? email, bool acknowledged, Guid correlationId, bool emailAlreadyValidated = false)
    {
        if (mode is not (ActivationDeliveryModes.Email or ActivationDeliveryModes.AdminIssued))
        {
            return IdentityAdministrationResult<T>.Failed(IdentityAdministrationOutcome.Invalid,
                "INVALID_ACTIVATION_DELIVERY_MODE", "Choose EMAIL or ADMIN_ISSUED activation delivery.", correlationId);
        }
        if (!_authenticationGateway.ActivationLinkEnabled)
        {
            return IdentityAdministrationResult<T>.Failed(IdentityAdministrationOutcome.IntegrationUnavailable,
                "ACCOUNT_LIFECYCLE_URL_NOT_CONFIGURED", "Account activation is not configured.", correlationId);
        }
        if (mode == ActivationDeliveryModes.Email)
        {
            if ((!emailAlreadyValidated && !IsUsableEmail(email)) || string.IsNullOrWhiteSpace(email))
            {
                return IdentityAdministrationResult<T>.Failed(IdentityAdministrationOutcome.Invalid,
                    "ACTIVATION_EMAIL_REQUIRED", "A usable email address is required for email activation.", correlationId);
            }
            if (!_authenticationGateway.EmailDeliveryEnabled)
            {
                return IdentityAdministrationResult<T>.Failed(IdentityAdministrationOutcome.IntegrationUnavailable,
                    "CREDENTIAL_CHALLENGE_EMAIL_DELIVERY_NOT_CONFIGURED", "Email activation delivery is not configured.", correlationId);
            }
        }
        if (mode == ActivationDeliveryModes.AdminIssued && !acknowledged)
        {
            return IdentityAdministrationResult<T>.Failed(IdentityAdministrationOutcome.Invalid,
                "ADMIN_ISSUED_HANDOFF_ACKNOWLEDGEMENT_REQUIRED",
                "Confirm that the activation material will be handed directly to the intended employee.", correlationId);
        }
        return null;
    }

    private static string? NormalizeOptionalEmail(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static string GenerateTemporaryPassword() =>
        $"Ep1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(14))}";

    private static bool IsUsableEmail(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 254) return false;
        try
        {
            var parsed = new MailAddress(value);
            return string.Equals(parsed.Address, value, StringComparison.OrdinalIgnoreCase) && parsed.Host.Contains('.');
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static IdentityAdministrationResult<T> Propagate<T>(IdentityAdministrationResult<bool> result) =>
        IdentityAdministrationResult<T>.Failed(result.Outcome, result.Classification, result.Message, result.CorrelationId);

    private static IdentityAdministrationResult<TTarget> Propagate<TTarget, TSource>(IdentityAdministrationResult<TSource> result) =>
        IdentityAdministrationResult<TTarget>.Failed(result.Outcome, result.Classification, result.Message, result.CorrelationId);
}
