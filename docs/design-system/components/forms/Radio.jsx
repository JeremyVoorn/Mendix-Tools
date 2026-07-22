import React from "react";

/** Single radio option. Group several with a shared `name`. */
export function Radio({ label, description, checked, defaultChecked, disabled = false, name, value, onChange, id, ...rest }) {
  const rId = id || React.useId();
  const isOn = checked ?? defaultChecked;
  return (
    <label htmlFor={rId} style={{ display: "inline-flex", alignItems: description ? "flex-start" : "center", gap: "10px", cursor: disabled ? "not-allowed" : "pointer", opacity: disabled ? 0.55 : 1 }}>
      <span style={{ position: "relative", display: "inline-flex", flexShrink: 0, marginTop: description ? "1px" : 0 }}>
        <input
          id={rId} type="radio" name={name} value={value} checked={checked} defaultChecked={defaultChecked}
          disabled={disabled} onChange={onChange}
          style={{ position: "absolute", opacity: 0, width: 18, height: 18, margin: 0, cursor: "inherit" }}
          {...rest}
        />
        <span style={{
          width: 18, height: 18, borderRadius: "50%",
          border: `1.5px solid ${isOn ? "var(--accent)" : "var(--border-strong)"}`,
          background: "var(--bg-surface)",
          display: "inline-flex", alignItems: "center", justifyContent: "center",
          transition: "all var(--duration-fast) var(--ease-standard)",
        }}>
          {isOn && <span style={{ width: 8, height: 8, borderRadius: "50%", background: "var(--accent)" }} />}
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
