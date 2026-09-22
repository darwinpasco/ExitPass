import { ChangeEvent, useEffect, useId, useRef, useState } from "react";
import { maskStatutoryIdReference } from "./statutoryIdMasking";

type AutomaticMaskedIdInputProps = {
  value: string;
  disabled?: boolean;
  onChange: (maskedValue: string) => void;
  onValidityChange?: (valid: boolean) => void;
};

export function AutomaticMaskedIdInput({ value, disabled = false, onChange, onValidityChange }: AutomaticMaskedIdInputProps) {
  const descriptionId = useId();
  const errorId = useId();
  const inputRef = useRef<HTMLInputElement>(null);
  const [rawValue, setRawValue] = useState("");
  const [isEditing, setIsEditing] = useState(!value);
  const [error, setError] = useState("");

  useEffect(() => {
    if (!value) {
      setRawValue("");
      setIsEditing(true);
      setError("");
    }
  }, [value]);

  function handleChange(event: ChangeEvent<HTMLInputElement>) {
    const nextValue = event.target.value;

    if (nextValue.includes("*")) {
      setError("Enter the ID reference without asterisks. WebPay masks it automatically.");
      onValidityChange?.(false);
      return;
    }

    if (nextValue && !/^[A-Za-z0-9-]+$/.test(nextValue)) {
      setError("Use letters, numbers, and hyphens only.");
      onValidityChange?.(false);
      return;
    }

    setRawValue(nextValue);
    setError("");
    onValidityChange?.(!nextValue || nextValue.trim().length >= 4);
  }

  function maskCurrentValue() {
    if (!isEditing || !rawValue) {
      return;
    }

    const result = maskStatutoryIdReference(rawValue);
    setRawValue("");

    if (!result.ok) {
      onChange("");
      setError(result.message);
      onValidityChange?.(false);
      return;
    }

    onChange(result.maskedValue);
    setIsEditing(false);
    setError("");
    onValidityChange?.(true);
  }

  function startReplacement() {
    onChange("");
    setRawValue("");
    setIsEditing(true);
    setError("");
    onValidityChange?.(true);
    requestAnimationFrame(() => inputRef.current?.focus());
  }

  const describedBy = error ? `${descriptionId} ${errorId}` : descriptionId;

  return (
    <div className="field automatic-mask-field">
      <label htmlFor="statutory-id-reference">ID No. / Control No. (optional)</label>
      <div className="automatic-mask-control">
        <input
          ref={inputRef}
          id="statutory-id-reference"
          name="maskedIdReference"
          value={isEditing ? rawValue : value}
          onChange={handleChange}
          onBlur={maskCurrentValue}
          placeholder="ID No. / Control No."
          autoComplete="off"
          autoCapitalize="characters"
          spellCheck={false}
          readOnly={!isEditing}
          disabled={disabled}
          aria-describedby={describedBy}
          aria-invalid={Boolean(error)}
        />
        {!isEditing && value && (
          <button type="button" className="secondary-button automatic-mask-change" onClick={startReplacement} disabled={disabled}>
            Change
          </button>
        )}
      </div>
      <small id={descriptionId}>Optional. If entered, use at least 4 characters. WebPay masks the reference automatically.</small>
      {error && (
        <p id={errorId} className="field-error" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
