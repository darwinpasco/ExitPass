import { useEffect, useMemo, useRef, useState } from "react";
import { controlledSalesInvoicePresentationVersion, controlledSalesInvoiceTemplateVersion, groupValidationCode, isManagementPlatformUiError, readinessStatusText, readinessTone, type EffectiveReadinessResult, type FiscalIdentityDetail, type FiscalIdentityMutationRequest, type SalesInvoiceHeaderProfile, type SalesInvoiceHeaderProfileMutationRequest, type SalesInvoiceHeaderProfileSummary, type SalesInvoiceProfileClient, type SalesInvoiceProfileReadScenarioName, type SalesInvoiceProfileUsageResult, type SalesInvoiceProfileValidationResult } from "./salesInvoiceProfiles";
import type { ReactNode } from "react";
import type { ManagementPlatformSite, ManagementPlatformUiError } from "./types";

interface SalesInvoiceProfilesPageProps {
  currentSite?: ManagementPlatformSite;
  client: SalesInvoiceProfileClient;
  developmentScenarioName?: SalesInvoiceProfileReadScenarioName;
  canManage?: boolean;
  onFormStateChange?: (state: { hasUnsavedChanges: boolean; mutationPending: boolean }) => void;
}

type LoadState<T> = {
  loading: boolean;
  value?: T;
  error?: ManagementPlatformUiError;
};

type ActiveForm = "fiscal-create" | "fiscal-edit" | "profile-create" | "profile-edit";
type MutationState = { pending: boolean; success?: string; error?: ManagementPlatformUiError };

export function SalesInvoiceProfilesPage({
  currentSite,
  client,
  developmentScenarioName,
  canManage = false,
  onFormStateChange
}: SalesInvoiceProfilesPageProps) {
  const [profilesState, setProfilesState] = useState<LoadState<SalesInvoiceHeaderProfileSummary[]>>({ loading: false });
  const [selectedProfileId, setSelectedProfileId] = useState<string | undefined>();
  const [profileState, setProfileState] = useState<LoadState<SalesInvoiceHeaderProfile>>({ loading: false });
  const [fiscalIdentityState, setFiscalIdentityState] = useState<LoadState<FiscalIdentityDetail>>({ loading: false });
  const [validationState, setValidationState] = useState<LoadState<SalesInvoiceProfileValidationResult>>({ loading: false });
  const [usageState, setUsageState] = useState<LoadState<SalesInvoiceProfileUsageResult>>({ loading: false });
  const [readinessState, setReadinessState] = useState<LoadState<EffectiveReadinessResult>>({ loading: false });
  const [effectiveAt, setEffectiveAt] = useState(() => toDateTimeLocal(new Date("2026-07-20T04:30:00Z")));
  const [activeForm, setActiveForm] = useState<ActiveForm | undefined>();
  const [fiscalForm, setFiscalForm] = useState<FiscalIdentityMutationRequest>(() => emptyFiscalForm());
  const [profileForm, setProfileForm] = useState<SalesInvoiceHeaderProfileMutationRequest>(() => emptyProfileForm(currentSite));
  const [formDirty, setFormDirty] = useState(false);
  const [fieldErrors, setFieldErrors] = useState<string[]>([]);
  const [mutationState, setMutationState] = useState<MutationState>({ pending: false });
  const [mutationAttemptCount, setMutationAttemptCount] = useState(0);
  const profileRequestSequence = useRef(0);
  const validationInFlightRef = useRef(false);
  const mutationInFlightRef = useRef(false);
  const selectedProfile = profileState.value;

  useEffect(() => {
    onFormStateChange?.({ hasUnsavedChanges: formDirty, mutationPending: mutationState.pending });
    return () => onFormStateChange?.({ hasUnsavedChanges: false, mutationPending: false });
  }, [formDirty, mutationState.pending, onFormStateChange]);

  useEffect(() => {
    if (!formDirty) {
      return;
    }

    const onBeforeUnload = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      event.returnValue = "";
    };
    window.addEventListener("beforeunload", onBeforeUnload);
    return () => window.removeEventListener("beforeunload", onBeforeUnload);
  }, [formDirty]);

  useEffect(() => {
    setSelectedProfileId(undefined);
    setProfileState({ loading: false });
    setFiscalIdentityState({ loading: false });
    setValidationState({ loading: false });
    setUsageState({ loading: false });
    setActiveForm(undefined);
    setFormDirty(false);
    setFieldErrors([]);
    setMutationState({ pending: false });
    setMutationAttemptCount(0);

    if (!currentSite) {
      setProfilesState({ loading: false, value: [] });
      setReadinessState({ loading: false });
      return;
    }

    const controller = new AbortController();
    setProfilesState({ loading: true });
    client.listProfiles(currentSite, controller.signal)
      .then((profiles) => setProfilesState({ loading: false, value: profiles }))
      .catch((error) => {
        if (!controller.signal.aborted) {
          setProfilesState({ loading: false, error: toSafeError(error) });
        }
      });

    loadReadiness(currentSite, effectiveAt, controller.signal);
    return () => controller.abort();
  }, [currentSite?.siteId, currentSite?.sitePosServerId, client]);

  useEffect(() => {
    if (!currentSite) {
      return;
    }

    const controller = new AbortController();
    loadReadiness(currentSite, effectiveAt, controller.signal);
    return () => controller.abort();
  }, [effectiveAt]);

  useEffect(() => {
    setValidationState({ loading: false });
    setFiscalIdentityState({ loading: false });
    setUsageState({ loading: false });

    if (!selectedProfileId) {
      setProfileState({ loading: false });
      return;
    }

    const requestId = profileRequestSequence.current + 1;
    profileRequestSequence.current = requestId;
    const controller = new AbortController();
    setProfileState({ loading: true });

    client.getProfile(selectedProfileId, controller.signal)
      .then((profile) => {
        if (profileRequestSequence.current !== requestId || controller.signal.aborted) {
          return;
        }
        setProfileState({ loading: false, value: profile });
        setFiscalIdentityState({ loading: true });
        setUsageState({ loading: true });
        return Promise.allSettled([
          client.getFiscalIdentity(profile.fiscalIdentityId, controller.signal),
          client.getProfileUsage(profile.salesInvoiceHeaderProfileId, controller.signal)
        ]);
      })
      .then((results) => {
        if (!results || profileRequestSequence.current !== requestId || controller.signal.aborted) {
          return;
        }
        const [fiscalResult, usageResult] = results;
        setFiscalIdentityState(fiscalResult.status === "fulfilled" ? { loading: false, value: fiscalResult.value } : { loading: false, error: toSafeError(fiscalResult.reason) });
        setUsageState(usageResult.status === "fulfilled" ? { loading: false, value: usageResult.value } : { loading: false, error: toSafeError(usageResult.reason) });
      })
      .catch((error) => {
        if (profileRequestSequence.current === requestId && !controller.signal.aborted) {
          setProfileState({ loading: false, error: toSafeError(error) });
        }
      });

    return () => controller.abort();
  }, [selectedProfileId, client]);

  function refreshProfiles() {
    if (!currentSite) {
      return;
    }

    const controller = new AbortController();
    setProfilesState({ loading: true });
    client.listProfiles(currentSite, controller.signal)
      .then((profiles) => setProfilesState({ loading: false, value: profiles }))
      .catch((error) => setProfilesState({ loading: false, error: toSafeError(error) }));
  }

  function loadReadiness(site: ManagementPlatformSite, nextEffectiveAt: string, signal?: AbortSignal) {
    setReadinessState({ loading: true });
    client.getEffectiveReadiness(site, toIsoFromDateTimeLocal(nextEffectiveAt), signal)
      .then((readiness) => setReadinessState({ loading: false, value: readiness }))
      .catch((error) => {
        if (!signal?.aborted) {
          setReadinessState({ loading: false, error: toSafeError(error) });
        }
      });
  }

  function validateSelectedProfile() {
    if (!selectedProfileId || validationState.loading) {
      return;
    }

    const controller = new AbortController();
    setValidationState({ loading: true });
    client.validateProfile(selectedProfileId, controller.signal)
      .then((validation) => setValidationState({ loading: false, value: validation }))
      .catch((error) => setValidationState({ loading: false, error: toSafeError(error) }));
  }

  function openFiscalCreate() {
    setFiscalForm(emptyFiscalForm());
    openForm("fiscal-create");
  }

  function openFiscalEdit(identity: FiscalIdentityDetail) {
    setFiscalForm({
      registeredBusinessName: identity.registeredBusinessName,
      registeredBusinessAddress: identity.registeredBusinessAddress,
      tin: identity.tin,
      taxpayerRegistrationPosture: identity.taxpayerRegistrationPosture
    });
    openForm("fiscal-edit");
  }

  function openProfileCreate() {
    setProfileForm(emptyProfileForm(currentSite, fiscalIdentityState.value?.fiscalIdentityId));
    openForm("profile-create");
  }

  function openProfileEdit(profile: SalesInvoiceHeaderProfile) {
    setProfileForm(formFromProfile(profile));
    openForm("profile-edit");
  }

  function openForm(form: ActiveForm) {
    setActiveForm(form);
    setFormDirty(false);
    setFieldErrors([]);
    setMutationState({ pending: false });
  }

  function cancelForm() {
    setActiveForm(undefined);
    setFormDirty(false);
    setFieldErrors([]);
    setMutationState({ pending: false });
  }

  async function submitFiscalForm() {
    if (!activeForm || mutationInFlightRef.current) {
      return;
    }

    const errors = validateFiscalForm(fiscalForm);
    if (errors.length > 0) {
      setFieldErrors(errors);
      return;
    }

    mutationInFlightRef.current = true;
    setMutationAttemptCount((count) => count + 1);
    setMutationState({ pending: true });
    try {
      const controller = new AbortController();
      const result = activeForm === "fiscal-edit" && fiscalIdentityState.value
        ? await client.updateFiscalIdentity(fiscalIdentityState.value.fiscalIdentityId, fiscalForm, controller.signal)
        : await client.createFiscalIdentity(fiscalForm, controller.signal);

      setFiscalIdentityState({ loading: false, value: result });
      setMutationState({ pending: false, success: activeForm === "fiscal-edit" ? "Fiscal Identity refreshed from authoritative update." : "Fiscal Identity created from authoritative response." });
      setFormDirty(false);
      setActiveForm(undefined);
    } catch (error) {
      setMutationState({ pending: false, error: toSafeError(error) });
    } finally {
      mutationInFlightRef.current = false;
    }
  }

  async function submitProfileForm() {
    if (!activeForm || !currentSite || mutationInFlightRef.current) {
      return;
    }

    const request = { ...profileForm, siteId: currentSite.siteId, sitePosServerId: currentSite.sitePosServerId ?? "" };
    const errors = validateProfileForm(request);
    if (errors.length > 0) {
      setFieldErrors(errors);
      return;
    }

    mutationInFlightRef.current = true;
    setMutationAttemptCount((count) => count + 1);
    setMutationState({ pending: true });
    try {
      const controller = new AbortController();
      const result = activeForm === "profile-edit" && selectedProfile
        ? await client.updateDraftProfile(selectedProfile.salesInvoiceHeaderProfileId, request, controller.signal)
        : await client.createProfile(request, controller.signal);

      setProfileState({ loading: false, value: result });
      setSelectedProfileId(result.salesInvoiceHeaderProfileId);
      setMutationState({ pending: false, success: activeForm === "profile-edit" ? "Draft profile refreshed from authoritative update." : "Draft profile created from authoritative response." });
      setFormDirty(false);
      setActiveForm(undefined);
      refreshProfiles();
    } catch (error) {
      setMutationState({ pending: false, error: toSafeError(error) });
    } finally {
      mutationInFlightRef.current = false;
    }
  }

  if (!currentSite) {
    return <StateBlock title="No authorized Site" message="Select an authorized Site before viewing Sales Invoice Header Profiles." tone="warning" />;
  }

  return (
    <section className="panel salesProfilePage" aria-labelledby="sales-profile-title">
      <div className="pageTitle">
        <div>
          <p className="eyebrow">Sales Invoice Profiles</p>
          <h2 id="sales-profile-title">Profile administration status</h2>
        </div>
        <div className="actionRow">
          {canManage && (
            <>
              <button type="button" onClick={openFiscalCreate}>Create Fiscal Identity</button>
              <button type="button" onClick={openProfileCreate}>Create Draft Profile</button>
            </>
          )}
          <button type="button" onClick={refreshProfiles} disabled={profilesState.loading}>Refresh</button>
        </div>
      </div>
      {developmentScenarioName && (
        <div className="developmentScenario compact" role="status" aria-label="Development profile scenario">
          Development profile scenario: <strong>{developmentScenarioName}</strong>
        </div>
      )}
      {developmentScenarioName && (
        <div className="developmentScenario compact" role="status" aria-label="Development mutation attempts">
          Development mutation attempts: <strong>{mutationAttemptCount}</strong>
        </div>
      )}
      <div className="siteSummary" aria-label="Current profile Site scope">
        <span>Current Site: <strong>{currentSite.displayName}</strong></span>
        <span>Site POS Server: <strong>{currentSite.sitePosServerId ?? "Not selected"}</strong></span>
      </div>
      <div className="salesProfileLayout">
        <ProfileList
          state={profilesState}
          selectedProfileId={selectedProfileId}
          onSelect={setSelectedProfileId}
        />
        <div className="detailStack">
          <ReadinessPanel state={readinessState} effectiveAt={effectiveAt} onEffectiveAtChange={setEffectiveAt} />
          {activeForm && (
            <MutationFormPanel
              activeForm={activeForm}
              currentSite={currentSite}
              fiscalForm={fiscalForm}
              profileForm={profileForm}
              fieldErrors={fieldErrors}
              mutationState={mutationState}
              onFiscalFormChange={(next) => { setFiscalForm(next); setFormDirty(true); }}
              onProfileFormChange={(next) => { setProfileForm(next); setFormDirty(true); }}
              onSubmitFiscal={submitFiscalForm}
              onSubmitProfile={submitProfileForm}
              onCancel={cancelForm}
            />
          )}
          {!activeForm && mutationState.success && <StateBlock title="Mutation accepted" message={mutationState.success} />}
          {!activeForm && mutationState.error && (
            mutationState.error.mutationUncertain
              ? <StateBlock title="Mutation result uncertain" message={safeErrorMessage(mutationState.error)} tone="warning" />
              : <StateBlock title="Mutation failed safely" message={safeErrorMessage(mutationState.error)} tone="danger" />
          )}
          {!selectedProfile && fiscalIdentityState.value && (
            <section className="subPanel" aria-labelledby="fiscal-result-title">
              <h3 id="fiscal-result-title">Fiscal Identity result</h3>
              <DetailList items={[
                ["Fiscal Identity ID", fiscalIdentityState.value.fiscalIdentityId],
                ["Registered business name", fiscalIdentityState.value.registeredBusinessName],
                ["Updated at", formatDateTime(fiscalIdentityState.value.updatedAt)],
                ["Created at", formatDateTime(fiscalIdentityState.value.createdAt)]
              ]} />
            </section>
          )}
          <ProfileDetail
            profileState={profileState}
            fiscalIdentityState={fiscalIdentityState}
            canManage={canManage}
            onEditFiscalIdentity={openFiscalEdit}
            onEditDraftProfile={openProfileEdit}
          />
          {selectedProfile && (
            <ValidationPanel state={validationState} onValidate={validateSelectedProfile} />
          )}
          {selectedProfile && <UsagePanel state={usageState} />}
        </div>
      </div>
    </section>
  );
}

function ProfileList({ state, selectedProfileId, onSelect }: {
  state: LoadState<SalesInvoiceHeaderProfileSummary[]>;
  selectedProfileId?: string;
  onSelect: (profileId: string) => void;
}) {
  if (state.loading) {
    return <StateBlock title="Loading profiles" message="Loading Site-scoped Sales Invoice Header Profiles." />;
  }

  if (state.error) {
    return <StateBlock title="Profile list unavailable" message={safeErrorMessage(state.error)} tone="danger" />;
  }

  const profiles = state.value ?? [];
  if (profiles.length === 0) {
    return <StateBlock title="No profiles" message="No Sales Invoice Header Profiles are available for the selected Site and Site POS Server." />;
  }

  return (
    <section className="subPanel" aria-labelledby="profile-list-title">
      <div className="sectionHeader">
        <h3 id="profile-list-title">Header Profiles</h3>
        <span className="countBadge">{profiles.length}</span>
      </div>
      <div className="tableScroller">
        <table className="dataTable">
          <thead>
            <tr>
              <th scope="col">Profile</th>
              <th scope="col">Lifecycle</th>
              <th scope="col">Fiscal Identity</th>
              <th scope="col">Effective window</th>
              <th scope="col">Versions</th>
              <th scope="col">Updated</th>
            </tr>
          </thead>
          <tbody>
            {profiles.map((profile) => (
              <tr key={profile.salesInvoiceHeaderProfileId} className={selectedProfileId === profile.salesInvoiceHeaderProfileId ? "selectedRow" : undefined}>
                <td>
                  <button type="button" className="textButton" onClick={() => onSelect(profile.salesInvoiceHeaderProfileId)}>
                    {profile.profileVersion}
                  </button>
                  <small>{profile.parkingLocationDisplay}</small>
                </td>
                <td><StatusPill value={profile.lifecycleState} /></td>
                <td>{profile.fiscalIdentityDisplayName ?? profile.fiscalIdentityId}</td>
                <td>{formatDateTime(profile.effectiveFrom)} to {formatDateTime(profile.effectiveTo)}</td>
                <td>{profile.templateVersion}<br />{profile.presentationVersion}</td>
                <td>{formatDateTime(profile.updatedAt)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  );
}

function ProfileDetail({ profileState, fiscalIdentityState, canManage, onEditFiscalIdentity, onEditDraftProfile }: {
  profileState: LoadState<SalesInvoiceHeaderProfile>;
  fiscalIdentityState: LoadState<FiscalIdentityDetail>;
  canManage: boolean;
  onEditFiscalIdentity: (identity: FiscalIdentityDetail) => void;
  onEditDraftProfile: (profile: SalesInvoiceHeaderProfile) => void;
}) {
  if (!profileState.loading && !profileState.value && !profileState.error) {
    return <StateBlock title="Select a profile" message="Choose a Header Profile to view details, Fiscal Identity, validation, readiness, and usage." />;
  }

  if (profileState.loading) {
    return <StateBlock title="Loading profile" message="Loading authoritative Header Profile details." />;
  }

  if (profileState.error) {
    return <StateBlock title="Profile unavailable" message={safeErrorMessage(profileState.error)} tone="danger" />;
  }

  const profile = profileState.value!;
  return (
    <section className="subPanel" aria-labelledby="profile-detail-title">
      <div className="sectionHeader">
        <h3 id="profile-detail-title">Profile detail</h3>
        <div className="actionRow">
          {canManage && fiscalIdentityState.value && (
            <button type="button" onClick={() => onEditFiscalIdentity(fiscalIdentityState.value!)}>Edit Fiscal Identity</button>
          )}
          {canManage && profile.lifecycleState === "DRAFT" && (
            <button type="button" onClick={() => onEditDraftProfile(profile)}>Edit Draft Profile</button>
          )}
          <span className="readOnlyBadge">{profile.lifecycleState === "DRAFT" ? "Draft" : "Read-only"}</span>
        </div>
      </div>
      {canManage && profile.lifecycleState === "APPROVED" && (
        <StateBlock title="Approved profile is read-only" message="Statutory changes require a new draft version. This slice does not create new versions." />
      )}
      {canManage && profile.lifecycleState === "RETIRED" && (
        <StateBlock title="Retired profile is read-only" message="Retired profiles cannot be edited or reactivated in this UI." />
      )}
      <div className="detailGrid">
        <DetailGroup title="Identity and scope" items={[
          ["Profile ID", profile.salesInvoiceHeaderProfileId],
          ["Version", profile.profileVersion],
          ["Lifecycle", profile.lifecycleState],
          ["Fiscal Identity ID", profile.fiscalIdentityId],
          ["Site", profile.siteId],
          ["Site POS Server", profile.sitePosServerId]
        ]} />
        <FiscalIdentityPanel state={fiscalIdentityState} />
        <DetailGroup title="Sales Invoice configuration" items={[
          ["Template version", profile.templateVersion],
          ["Presentation version", profile.presentationVersion],
          ["POS serial number", profile.posSerialNumber],
          ["Machine Identification Number", profile.machineIdentificationNumber],
          ["Parking location", profile.parkingLocationDisplay]
        ]} />
        <DetailGroup title="BIR accreditation" items={[
          ["BIR accreditation number", profile.birAccreditationNumber],
          ["BIR accreditation issued date", profile.birAccreditationIssuedDate],
          ["BIR accreditation valid-until date", profile.birAccreditationValidUntil]
        ]} />
        <DetailGroup title="PTU" items={[
          ["PTU number", profile.ptuNumber],
          ["PTU issued date", profile.ptuIssuedDate]
        ]} />
        <DetailGroup title="Presentation wording" items={[
          ["Sales Invoice legal statement", profile.salesInvoiceLegalStatement],
          ["Customer-service footer", profile.customerServiceFooter]
        ]} />
        <DetailGroup title="Effective and lifecycle metadata" items={[
          ["Effective from", formatDateTime(profile.effectiveFrom)],
          ["Effective to", formatDateTime(profile.effectiveTo)],
          ["Approved at", formatDateTime(profile.approvedAt)],
          ["Retired at", formatDateTime(profile.retiredAt)],
          ["Created at", formatDateTime(profile.createdAt)],
          ["Updated at", formatDateTime(profile.updatedAt)]
        ]} />
      </div>
    </section>
  );
}

function FiscalIdentityPanel({ state }: { state: LoadState<FiscalIdentityDetail> }) {
  if (state.loading) {
    return <DetailGroup title="Registered business" items={[["Status", "Loading Fiscal Identity"]]} />;
  }
  if (state.error) {
    return <DetailGroup title="Registered business" items={[["Status", safeErrorMessage(state.error)]]} />;
  }
  const identity = state.value;
  return <DetailGroup title="Registered business" items={[
    ["Registered business name", identity?.registeredBusinessName],
    ["Registered business address", identity?.registeredBusinessAddress],
    ["TIN", identity?.tin],
    ["Taxpayer/VAT posture", identity?.taxpayerRegistrationPosture],
    ["Lifecycle/status", identity?.lifecycleState ?? identity?.status],
    ["Created at", formatDateTime(identity?.createdAt)],
    ["Updated at", formatDateTime(identity?.updatedAt)]
  ]} />;
}

function ValidationPanel({ state, onValidate }: { state: LoadState<SalesInvoiceProfileValidationResult>; onValidate: () => void }) {
  const groupedCodes = useMemo(() => groupCodes(state.value?.missingOrInvalidFieldCodes ?? []), [state.value?.missingOrInvalidFieldCodes]);
  return (
    <section className="subPanel" aria-labelledby="validation-title">
      <div className="sectionHeader">
        <h3 id="validation-title">Authoritative completeness validation</h3>
        <button type="button" onClick={onValidate} disabled={state.loading}>Validate configuration</button>
      </div>
      {state.loading && <StateBlock title="Validating" message="Requesting authoritative profile completeness validation." />}
      {state.error && <StateBlock title="Validation unavailable" message={safeErrorMessage(state.error)} tone="danger" />}
      {state.value && (
        <div className="validationResult" role="status" aria-label="Validation result">
          <p><strong>Configuration completeness:</strong> {state.value.isComplete ? "Complete" : "Incomplete"}</p>
          <p><strong>Lifecycle:</strong> {state.value.lifecycleState}</p>
          <p><strong>Validated at:</strong> {formatDateTime(state.value.validatedAt)}</p>
          <p><strong>Template version:</strong> {state.value.templateVersionPosture}</p>
          <p><strong>Presentation version:</strong> {state.value.presentationVersionPosture}</p>
          <p><strong>Effective window:</strong> {state.value.effectiveWindowPosture}</p>
          <p><strong>Overlap:</strong> {state.value.overlapPosture}</p>
          <p><strong>Fiscal Identity:</strong> {state.value.fiscalIdentityPosture}</p>
          {state.value.correlationId && <p><strong>Support reference:</strong> {state.value.correlationId}</p>}
          <div className="findingGroups">
            {groupedCodes.length === 0 ? (
              <p>No missing or invalid field codes were returned.</p>
            ) : groupedCodes.map((group) => (
              <section key={group.name} aria-labelledby={`validation-${slug(group.name)}`}>
                <h4 id={`validation-${slug(group.name)}`}>{group.name}</h4>
                <ul>
                  {group.codes.map((code) => <li key={code}><code>{code}</code></li>)}
                </ul>
              </section>
            ))}
          </div>
          {state.value.messages.length > 0 && (
            <ul className="messageList">
              {state.value.messages.map((message) => <li key={message}>{message}</li>)}
            </ul>
          )}
        </div>
      )}
    </section>
  );
}

function ReadinessPanel({ state, effectiveAt, onEffectiveAtChange }: {
  state: LoadState<EffectiveReadinessResult>;
  effectiveAt: string;
  onEffectiveAtChange: (value: string) => void;
}) {
  return (
    <section className="subPanel" aria-labelledby="readiness-title">
      <div className="sectionHeader">
        <h3 id="readiness-title">Effective readiness</h3>
        <label className="inlineField" htmlFor="effective-at">Effective at
          <input id="effective-at" type="datetime-local" value={effectiveAt} onChange={(event) => onEffectiveAtChange(event.target.value)} />
        </label>
      </div>
      {state.loading && <StateBlock title="Loading readiness" message="Loading authoritative effective readiness." />}
      {state.error && <StateBlock title="Readiness unavailable" message={safeErrorMessage(state.error)} tone="danger" />}
      {state.value && (
        <div className="readinessGrid" role="status" aria-label="Effective readiness result">
          <span className={`readinessBanner ${readinessTone(state.value.resolutionStatus)}`}>{readinessStatusText(state.value.resolutionStatus)}</span>
          <DetailList items={[
            ["Status", state.value.resolutionStatus],
            ["Effective profile ID", state.value.effectiveProfileId],
            ["Profile version", state.value.profileVersion],
            ["Fiscal Identity ID", state.value.fiscalIdentityId],
            ["Lifecycle", state.value.lifecycleState],
            ["Completeness", state.value.isComplete ? "Complete" : "Incomplete"],
            ["Enforcement required", state.value.enforcementRequired ? "Yes" : "No"],
            ["BIR validity", state.value.birAccreditationValidityPosture],
            ["PTU posture", state.value.ptuCompletenessPosture],
            ["Version posture", state.value.supportedVersionPosture],
            ["Overlap posture", state.value.overlapOrAmbiguityPosture],
            ["Last updated", formatDateTime(state.value.lastUpdatedAt)],
            ["Support reference", state.value.correlationId]
          ]} />
          {state.value.missingOrInvalidFieldCodes.length > 0 && (
            <ul className="messageList">
              {state.value.missingOrInvalidFieldCodes.map((code) => <li key={code}><code>{code}</code></li>)}
            </ul>
          )}
        </div>
      )}
    </section>
  );
}

function UsagePanel({ state }: { state: LoadState<SalesInvoiceProfileUsageResult> }) {
  return (
    <section className="subPanel" aria-labelledby="usage-title">
      <div className="sectionHeader">
        <h3 id="usage-title">Immutable usage</h3>
        <span className="readOnlyBadge">Read-only</span>
      </div>
      {state.loading && <StateBlock title="Loading usage" message="Loading immutable snapshot usage summary." />}
      {state.error && <StateBlock title="Usage unavailable" message={safeErrorMessage(state.error)} tone="danger" />}
      {state.value && (
        <div>
          <DetailList items={[
            ["Profile ID", state.value.salesInvoiceHeaderProfileId],
            ["Profile version", state.value.profileVersion],
            ["Fiscal Identity ID", state.value.fiscalIdentityId],
            ["Fiscal-document count", state.value.fiscalDocumentCount.toString()],
            ["First snapshot", formatDateTime(state.value.firstSnapshotAt)],
            ["Latest snapshot", formatDateTime(state.value.latestSnapshotAt)],
            ["Destructive mutation blocked", state.value.destructiveMutationBlocked ? "Yes" : "No"],
            ["Support reference", state.value.correlationId]
          ]} />
          {state.value.safeFiscalDocumentIds.length > 0 && (
            <ul className="messageList" aria-label="Safe fiscal-document identifiers">
              {state.value.safeFiscalDocumentIds.map((id) => <li key={id}>{id}</li>)}
            </ul>
          )}
        </div>
      )}
    </section>
  );
}

function MutationFormPanel({
  activeForm,
  currentSite,
  fiscalForm,
  profileForm,
  fieldErrors,
  mutationState,
  onFiscalFormChange,
  onProfileFormChange,
  onSubmitFiscal,
  onSubmitProfile,
  onCancel
}: {
  activeForm: ActiveForm;
  currentSite: ManagementPlatformSite;
  fiscalForm: FiscalIdentityMutationRequest;
  profileForm: SalesInvoiceHeaderProfileMutationRequest;
  fieldErrors: string[];
  mutationState: MutationState;
  onFiscalFormChange: (next: FiscalIdentityMutationRequest) => void;
  onProfileFormChange: (next: SalesInvoiceHeaderProfileMutationRequest) => void;
  onSubmitFiscal: () => void;
  onSubmitProfile: () => void;
  onCancel: () => void;
}) {
  const isFiscal = activeForm === "fiscal-create" || activeForm === "fiscal-edit";
  const title = formTitle(activeForm);
  return (
    <section className="subPanel mutationPanel" aria-labelledby="mutation-form-title">
      <div className="sectionHeader">
        <h3 id="mutation-form-title">{title}</h3>
        <span className="statusPill compactPill">Manage</span>
      </div>
      <p className="formHelp">Central PMS derives the actor identity. This browser form sends only governed Management Platform fields.</p>
      {fieldErrors.length > 0 && (
        <div className="formSummary" role="alert" aria-label="Form validation summary">
          <h4>Review submitted fields.</h4>
          <ul>{fieldErrors.map((error) => <li key={error}>{error}</li>)}</ul>
        </div>
      )}
      {mutationState.error && (
        mutationState.error.mutationUncertain
          ? <StateBlock title="Mutation result uncertain" message={safeErrorMessage(mutationState.error)} tone="warning" />
          : <StateBlock title="Mutation failed safely" message={safeErrorMessage(mutationState.error)} tone="danger" />
      )}
      {mutationState.success && <StateBlock title="Mutation accepted" message={mutationState.success} />}
      {isFiscal ? (
        <FiscalIdentityForm
          value={fiscalForm}
          pending={mutationState.pending}
          submitLabel={activeForm === "fiscal-create" ? "Create Fiscal Identity" : "Save Fiscal Identity"}
          onChange={onFiscalFormChange}
          onSubmit={onSubmitFiscal}
          onCancel={onCancel}
        />
      ) : (
        <HeaderProfileForm
          value={{ ...profileForm, siteId: currentSite.siteId, sitePosServerId: currentSite.sitePosServerId ?? "" }}
          pending={mutationState.pending}
          submitLabel={activeForm === "profile-create" ? "Create Draft Profile" : "Save Draft Changes"}
          onChange={onProfileFormChange}
          onSubmit={onSubmitProfile}
          onCancel={onCancel}
        />
      )}
    </section>
  );
}

function FiscalIdentityForm({ value, pending, submitLabel, onChange, onSubmit, onCancel }: {
  value: FiscalIdentityMutationRequest;
  pending: boolean;
  submitLabel: string;
  onChange: (next: FiscalIdentityMutationRequest) => void;
  onSubmit: () => void;
  onCancel: () => void;
}) {
  return (
    <form className="managedForm" aria-label={submitLabel} onSubmit={(event) => { event.preventDefault(); onSubmit(); }}>
      <FormSection title="Fiscal Identity">
        <TextField id="registered-business-name" label="Registered business name" value={value.registeredBusinessName} onChange={(registeredBusinessName) => onChange({ ...value, registeredBusinessName })} required />
        <TextField id="registered-business-address" label="Registered business address" value={value.registeredBusinessAddress} onChange={(registeredBusinessAddress) => onChange({ ...value, registeredBusinessAddress })} required textarea />
        <TextField id="tin" label="TIN" value={value.tin} onChange={(tin) => onChange({ ...value, tin })} required />
        <TextField id="taxpayer-posture" label="Taxpayer/VAT registration posture" value={value.taxpayerRegistrationPosture} onChange={(taxpayerRegistrationPosture) => onChange({ ...value, taxpayerRegistrationPosture })} required />
      </FormSection>
      <FormActions pending={pending} submitLabel={submitLabel} onCancel={onCancel} />
    </form>
  );
}

function HeaderProfileForm({ value, pending, submitLabel, onChange, onSubmit, onCancel }: {
  value: SalesInvoiceHeaderProfileMutationRequest;
  pending: boolean;
  submitLabel: string;
  onChange: (next: SalesInvoiceHeaderProfileMutationRequest) => void;
  onSubmit: () => void;
  onCancel: () => void;
}) {
  return (
    <form className="managedForm" aria-label={submitLabel} onSubmit={(event) => { event.preventDefault(); onSubmit(); }}>
      <FormSection title="Fiscal Identity and scope">
        <TextField id="profile-fiscal-identity" label="Fiscal Identity ID" value={value.fiscalIdentityId} onChange={(fiscalIdentityId) => onChange({ ...value, fiscalIdentityId })} required />
        <TextField id="profile-site-id" label="Site ID" value={value.siteId} onChange={() => undefined} readOnly />
        <TextField id="profile-site-pos-server-id" label="Site POS Server ID" value={value.sitePosServerId} onChange={() => undefined} readOnly />
        <TextField id="profile-version" label="Profile version" value={value.profileVersion} onChange={(profileVersion) => onChange({ ...value, profileVersion })} required />
      </FormSection>
      <FormSection title="Supported template versions">
        <SelectField id="template-version" label="Template version" value={value.templateVersion} onChange={(templateVersion) => onChange({ ...value, templateVersion })} options={[controlledSalesInvoiceTemplateVersion]} />
        <SelectField id="presentation-version" label="Presentation version" value={value.presentationVersion} onChange={(presentationVersion) => onChange({ ...value, presentationVersion })} options={[controlledSalesInvoicePresentationVersion]} />
      </FormSection>
      <FormSection title="Device registration">
        <TextField id="pos-serial-number" label="POS serial number" value={value.posSerialNumber} onChange={(posSerialNumber) => onChange({ ...value, posSerialNumber })} required />
        <TextField id="machine-identification-number" label="Machine Identification Number" value={value.machineIdentificationNumber} onChange={(machineIdentificationNumber) => onChange({ ...value, machineIdentificationNumber })} required />
      </FormSection>
      <FormSection title="Parking-location display">
        <TextField id="parking-location-display" label="Parking-location display" value={value.parkingLocationDisplay} onChange={(parkingLocationDisplay) => onChange({ ...value, parkingLocationDisplay })} required />
      </FormSection>
      <FormSection title="BIR accreditation">
        <TextField id="bir-accreditation-number" label="BIR accreditation number" value={value.birAccreditationNumber} onChange={(birAccreditationNumber) => onChange({ ...value, birAccreditationNumber })} required />
        <TextField id="bir-accreditation-issued-date" label="BIR accreditation date issued" value={value.birAccreditationIssuedDate} onChange={(birAccreditationIssuedDate) => onChange({ ...value, birAccreditationIssuedDate })} required type="date" />
        <TextField id="bir-accreditation-valid-until" label="BIR accreditation valid until" value={value.birAccreditationValidUntil} onChange={(birAccreditationValidUntil) => onChange({ ...value, birAccreditationValidUntil })} required type="date" />
      </FormSection>
      <FormSection title="PTU">
        <TextField id="ptu-number" label="PTU number" value={value.ptuNumber} onChange={(ptuNumber) => onChange({ ...value, ptuNumber })} required />
        <TextField id="ptu-issued-date" label="PTU date issued" value={value.ptuIssuedDate} onChange={(ptuIssuedDate) => onChange({ ...value, ptuIssuedDate })} required type="date" />
      </FormSection>
      <FormSection title="Sales Invoice wording">
        <TextField id="sales-invoice-legal-statement" label="Sales Invoice legal statement" value={value.salesInvoiceLegalStatement} onChange={(salesInvoiceLegalStatement) => onChange({ ...value, salesInvoiceLegalStatement })} required textarea />
        <TextField id="customer-service-footer" label="Customer-service footer" value={value.customerServiceFooter} onChange={(customerServiceFooter) => onChange({ ...value, customerServiceFooter })} textarea />
      </FormSection>
      <FormSection title="Effective period">
        <TextField id="effective-from" label="Effective from" value={value.effectiveFrom} onChange={(effectiveFrom) => onChange({ ...value, effectiveFrom })} required type="datetime-local" />
        <TextField id="effective-to" label="Effective to" value={value.effectiveTo ?? ""} onChange={(effectiveTo) => onChange({ ...value, effectiveTo })} type="datetime-local" />
      </FormSection>
      <FormActions pending={pending} submitLabel={submitLabel} onCancel={onCancel} />
    </form>
  );
}

function FormSection({ title, children }: { title: string; children: ReactNode }) {
  return (
    <fieldset className="formSection">
      <legend>{title}</legend>
      <div className="formGrid">{children}</div>
    </fieldset>
  );
}

function TextField({ id, label, value, onChange, required = false, readOnly = false, textarea = false, type = "text" }: {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  required?: boolean;
  readOnly?: boolean;
  textarea?: boolean;
  type?: string;
}) {
  return (
    <label className="formField" htmlFor={id}>
      <span>{label}{required ? " *" : ""}</span>
      {textarea ? (
        <textarea id={id} value={value} readOnly={readOnly} aria-required={required} onChange={(event) => onChange(event.target.value)} />
      ) : (
        <input id={id} type={type} value={value} readOnly={readOnly} aria-required={required} onChange={(event) => onChange(event.target.value)} />
      )}
    </label>
  );
}

function SelectField({ id, label, value, options, onChange }: {
  id: string;
  label: string;
  value: string;
  options: string[];
  onChange: (value: string) => void;
}) {
  return (
    <label className="formField" htmlFor={id}>
      <span>{label}</span>
      <select id={id} value={value} onChange={(event) => onChange(event.target.value)}>
        {options.map((option) => <option key={option} value={option}>{option}</option>)}
      </select>
    </label>
  );
}

function FormActions({ pending, submitLabel, onCancel }: { pending: boolean; submitLabel: string; onCancel: () => void }) {
  return (
    <div className="formActions">
      <button type="submit" disabled={pending}>{pending ? "Saving..." : submitLabel}</button>
      <button type="button" className="secondaryButton" onClick={onCancel} disabled={pending}>Cancel</button>
      {pending && <span role="status">Saving authoritative change.</span>}
    </div>
  );
}

function DetailGroup({ title, items }: { title: string; items: Array<[string, string | undefined]> }) {
  const headingId = `detail-${slug(title)}`;
  return (
    <section className="detailGroup" aria-labelledby={headingId}>
      <h4 id={headingId}>{title}</h4>
      <DetailList items={items} />
    </section>
  );
}

function DetailList({ items }: { items: Array<[string, string | undefined]> }) {
  return (
    <dl>
      {items.map(([label, value]) => (
        <div key={label}>
          <dt>{label}</dt>
          <dd>{value || "Not returned"}</dd>
        </div>
      ))}
    </dl>
  );
}

function StatusPill({ value }: { value: string }) {
  return <span className="statusPill compactPill">{value}</span>;
}

function StateBlock({ title, message, tone = "neutral" }: { title: string; message: string; tone?: "neutral" | "warning" | "danger" }) {
  return (
    <section className={`stateMessage embedded ${tone}`} role={tone === "danger" ? "alert" : "status"} aria-label={title}>
      <h3>{title}</h3>
      <p>{message}</p>
    </section>
  );
}

function groupCodes(codes: string[]): Array<{ name: string; codes: string[] }> {
  const groups = new Map<string, string[]>();
  for (const code of codes) {
    const group = groupValidationCode(code);
    groups.set(group, [...(groups.get(group) ?? []), code]);
  }
  return [...groups.entries()].map(([name, groupedCodes]) => ({ name, codes: groupedCodes }));
}

function emptyFiscalForm(): FiscalIdentityMutationRequest {
  return {
    registeredBusinessName: "",
    registeredBusinessAddress: "",
    tin: "",
    taxpayerRegistrationPosture: "VAT_REGISTERED"
  };
}

function emptyProfileForm(site?: ManagementPlatformSite, fiscalIdentityId = ""): SalesInvoiceHeaderProfileMutationRequest {
  return {
    fiscalIdentityId,
    siteId: site?.siteId ?? "",
    sitePosServerId: site?.sitePosServerId ?? "",
    profileVersion: "",
    templateVersion: controlledSalesInvoiceTemplateVersion,
    presentationVersion: controlledSalesInvoicePresentationVersion,
    posSerialNumber: "",
    machineIdentificationNumber: "",
    parkingLocationDisplay: "",
    birAccreditationNumber: "",
    birAccreditationIssuedDate: "",
    birAccreditationValidUntil: "",
    ptuNumber: "",
    ptuIssuedDate: "",
    salesInvoiceLegalStatement: "",
    customerServiceFooter: "",
    effectiveFrom: "",
    effectiveTo: ""
  };
}

function formFromProfile(profile: SalesInvoiceHeaderProfile): SalesInvoiceHeaderProfileMutationRequest {
  return {
    fiscalIdentityId: profile.fiscalIdentityId,
    siteId: profile.siteId,
    sitePosServerId: profile.sitePosServerId,
    profileVersion: profile.profileVersion,
    templateVersion: profile.templateVersion,
    presentationVersion: profile.presentationVersion,
    posSerialNumber: profile.posSerialNumber ?? "",
    machineIdentificationNumber: profile.machineIdentificationNumber ?? "",
    parkingLocationDisplay: profile.parkingLocationDisplay,
    birAccreditationNumber: profile.birAccreditationNumber ?? "",
    birAccreditationIssuedDate: profile.birAccreditationIssuedDate ?? "",
    birAccreditationValidUntil: profile.birAccreditationValidUntil ?? "",
    ptuNumber: profile.ptuNumber ?? "",
    ptuIssuedDate: profile.ptuIssuedDate ?? "",
    salesInvoiceLegalStatement: profile.salesInvoiceLegalStatement ?? "",
    customerServiceFooter: profile.customerServiceFooter ?? "",
    effectiveFrom: toDateTimeLocalValue(profile.effectiveFrom),
    effectiveTo: toDateTimeLocalValue(profile.effectiveTo)
  };
}

function validateFiscalForm(form: FiscalIdentityMutationRequest): string[] {
  return requiredErrors([
    ["Registered business name", form.registeredBusinessName],
    ["Registered business address", form.registeredBusinessAddress],
    ["TIN", form.tin],
    ["Taxpayer/VAT registration posture", form.taxpayerRegistrationPosture]
  ]);
}

function validateProfileForm(form: SalesInvoiceHeaderProfileMutationRequest): string[] {
  return requiredErrors([
    ["Fiscal Identity ID", form.fiscalIdentityId],
    ["Site ID", form.siteId],
    ["Site POS Server ID", form.sitePosServerId],
    ["Profile version", form.profileVersion],
    ["Template version", form.templateVersion],
    ["Presentation version", form.presentationVersion],
    ["POS serial number", form.posSerialNumber],
    ["Machine Identification Number", form.machineIdentificationNumber],
    ["Parking-location display", form.parkingLocationDisplay],
    ["BIR accreditation number", form.birAccreditationNumber],
    ["BIR accreditation date issued", form.birAccreditationIssuedDate],
    ["BIR accreditation valid until", form.birAccreditationValidUntil],
    ["PTU number", form.ptuNumber],
    ["PTU date issued", form.ptuIssuedDate],
    ["Sales Invoice legal statement", form.salesInvoiceLegalStatement],
    ["Effective from", form.effectiveFrom]
  ]);
}

function requiredErrors(fields: Array<[string, string | undefined]>): string[] {
  return fields
    .filter(([, value]) => !value?.trim())
    .map(([label]) => `${label} is required.`);
}

function formTitle(form: ActiveForm): string {
  switch (form) {
    case "fiscal-create":
      return "Create Fiscal Identity";
    case "fiscal-edit":
      return "Edit Fiscal Identity";
    case "profile-create":
      return "Create Draft Header Profile";
    case "profile-edit":
      return "Edit Draft Header Profile";
  }
}

function toSafeError(error: unknown): ManagementPlatformUiError {
  if (isManagementPlatformUiError(error)) {
    return error;
  }
  return {
    kind: "unknown",
    code: "SALES_INVOICE_PROFILE_READ_UI_ERROR",
    message: "The profile information could not be loaded safely.",
    retryable: false,
    mutationUncertain: false
  };
}

function safeErrorMessage(error: ManagementPlatformUiError): string {
  const support = error.correlationId ? ` Support reference: ${error.correlationId}.` : "";
  return `${error.message}${support}`;
}

function formatDateTime(value?: string): string {
  if (!value) {
    return "Not returned";
  }
  return value;
}

function toDateTimeLocal(value: Date): string {
  return value.toISOString().slice(0, 16);
}

function toIsoFromDateTimeLocal(value: string): string {
  if (!value) {
    return new Date("2026-07-20T04:30:00Z").toISOString();
  }
  return new Date(value).toISOString();
}

function toDateTimeLocalValue(value?: string): string {
  if (!value) {
    return "";
  }

  return value.slice(0, 16);
}

function slug(value: string): string {
  return value.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/(^-|-$)/g, "");
}
