import { useEffect, useMemo, useRef, useState } from "react";
import { groupValidationCode, isManagementPlatformUiError, readinessStatusText, readinessTone, type EffectiveReadinessResult, type FiscalIdentityDetail, type SalesInvoiceHeaderProfile, type SalesInvoiceHeaderProfileSummary, type SalesInvoiceProfileReadClient, type SalesInvoiceProfileReadScenarioName, type SalesInvoiceProfileUsageResult, type SalesInvoiceProfileValidationResult } from "./salesInvoiceProfiles";
import type { ManagementPlatformSite, ManagementPlatformUiError } from "./types";

interface SalesInvoiceProfilesPageProps {
  currentSite?: ManagementPlatformSite;
  client: SalesInvoiceProfileReadClient;
  developmentScenarioName?: SalesInvoiceProfileReadScenarioName;
}

type LoadState<T> = {
  loading: boolean;
  value?: T;
  error?: ManagementPlatformUiError;
};

export function SalesInvoiceProfilesPage({ currentSite, client, developmentScenarioName }: SalesInvoiceProfilesPageProps) {
  const [profilesState, setProfilesState] = useState<LoadState<SalesInvoiceHeaderProfileSummary[]>>({ loading: false });
  const [selectedProfileId, setSelectedProfileId] = useState<string | undefined>();
  const [profileState, setProfileState] = useState<LoadState<SalesInvoiceHeaderProfile>>({ loading: false });
  const [fiscalIdentityState, setFiscalIdentityState] = useState<LoadState<FiscalIdentityDetail>>({ loading: false });
  const [validationState, setValidationState] = useState<LoadState<SalesInvoiceProfileValidationResult>>({ loading: false });
  const [usageState, setUsageState] = useState<LoadState<SalesInvoiceProfileUsageResult>>({ loading: false });
  const [readinessState, setReadinessState] = useState<LoadState<EffectiveReadinessResult>>({ loading: false });
  const [effectiveAt, setEffectiveAt] = useState(() => toDateTimeLocal(new Date("2026-07-20T04:30:00Z")));
  const profileRequestSequence = useRef(0);
  const validationInFlightRef = useRef(false);
  const selectedProfile = profileState.value;

  useEffect(() => {
    setSelectedProfileId(undefined);
    setProfileState({ loading: false });
    setFiscalIdentityState({ loading: false });
    setValidationState({ loading: false });
    setUsageState({ loading: false });

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

  if (!currentSite) {
    return <StateBlock title="No authorized Site" message="Select an authorized Site before viewing Sales Invoice Header Profiles." tone="warning" />;
  }

  return (
    <section className="panel salesProfilePage" aria-labelledby="sales-profile-title">
      <div className="pageTitle">
        <div>
          <p className="eyebrow">Sales Invoice Profiles</p>
          <h2 id="sales-profile-title">Read-only profile administration status</h2>
        </div>
        <button type="button" onClick={refreshProfiles} disabled={profilesState.loading}>Refresh</button>
      </div>
      {developmentScenarioName && (
        <div className="developmentScenario compact" role="status" aria-label="Development profile scenario">
          Development profile scenario: <strong>{developmentScenarioName}</strong>
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
          <ProfileDetail profileState={profileState} fiscalIdentityState={fiscalIdentityState} />
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

function ProfileDetail({ profileState, fiscalIdentityState }: {
  profileState: LoadState<SalesInvoiceHeaderProfile>;
  fiscalIdentityState: LoadState<FiscalIdentityDetail>;
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
        <span className="readOnlyBadge">Read-only</span>
      </div>
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

function slug(value: string): string {
  return value.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/(^-|-$)/g, "");
}