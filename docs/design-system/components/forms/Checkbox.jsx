import React from "react";

/**
 * Checkbox with label. Controlled via `checked`/`onChange` or uncontrolled.
 * Supports an indeterminate state (for "select all" headers).
 */
export function Checkbox({ label, checked, defaultChecked, indeterminate = false, disabled = false, onChange, id, description, ...rest }) {
  const cbId = id || React.useId();
  const ref = React.useRef(null);
  React.useEffect(() => { if (ref.current) ref.current.indeterminate = indeterminate; }, [indeterminate]);
  const isOn = checked ?? defaultChecked;

  return (
    <label htmlFor={cbId} style={{ display: "inline-flex", alignItems: description ? "flex-start" : "center", gap: "10px", cursor: disabled ? "not-allowed" : "pointer", opacity: disabled ? 0.55 : 1 }}>
      <span style={{ position: "relative", display: "inline-flex", flexShrink: 0, marginTop: description ? "1px" : 0 }}>
        <input
          ref={ref} id={cbId} type="checkbox" checked={checked} defaultChecked={defaultChecked}
          disabled={disabled} onChange={onChange}
          style={{ position: "absolute", opacity: 0, width: 18, height: 18, margin: 0, cursor: "inherit" }}
          {...rest}
        />
        <span style={{
          width: 18, height: 18, borderRadius: "var(--radius-sm)",
          border: `1.5px solid ${isOn || indeterminate ? "var(--accent)" : "var(--border-strong)"}`,
          background: isOn || indeterminate ? "var(--accent)" : "var(--bg-surface)",
          display: "inline-flex", alignItems: "center", justifyContent: "center",
          transition: "all var(--duration-fast) var(--ease-standard)", color: "#fff",
        }}>
          {indeterminate
            ? <i data-lucide="minus" style={{ width: 13, height: 13 }} />
            : isOn && <i data-lucide="check" style={{ width: 13, height: 13 }} />}
        </span>
      </span>
      {label && (
        <span style={{ display: "flex", flexDirection: "column", gap: "2px" }}>
          <span style={{ fontSize: "var(--text-base)", color: "var(--text-primary)", lineHeight: 1.35 }}>{label}</span>
          {description && <span style={{ fontSize: "var(--text-xs)", color: "var(--text-tertiary)" }}>{description}</span>}
        </span>
      )}
    </label>
  );
}
