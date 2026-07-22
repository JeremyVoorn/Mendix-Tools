import React from "react";

const sizeMap = {
  sm: { height: "var(--control-sm)", font: "var(--text-sm)" },
  md: { height: "var(--control-md)", font: "var(--text-base)" },
  lg: { height: "var(--control-lg)", font: "var(--text-md)" },
};

/**
 * Native <select> styled to match Input, with a chevron affordance.
 * Options passed as [{value,label}] or plain children.
 */
export function Select({
  label,
  hint,
  error,
  size = "md",
  options,
  disabled = false,
  id,
  children,
  containerStyle,
  style,
  ...rest
}) {
  const s = sizeMap[size] || sizeMap.md;
  const [focus, setFocus] = React.useState(false);
  const selectId = id || React.useId();
  const borderColor = error ? "var(--danger)" : focus ? "var(--accent)" : "var(--border-strong)";

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "6px", ...containerStyle }}>
      {label && (
        <label htmlFor={selectId} style={{ fontSize: "var(--text-sm)", fontWeight: "var(--fw-medium)", color: "var(--text-secondary)" }}>
          {label}
        </label>
      )}
      <div style={{ position: "relative", display: "flex", alignItems: "center" }}>
        <select
          id={selectId}
          disabled={disabled}
          onFocus={() => setFocus(true)}
          onBlur={() => setFocus(false)}
          style={{
            appearance: "none", WebkitAppearance: "none",
            width: "100%", height: s.height, padding: "0 34px 0 12px",
            background: disabled ? "var(--bg-subtle)" : "var(--bg-surface)",
            color: "var(--text-primary)", fontSize: s.font, fontFamily: "var(--font-sans)",
            border: `var(--border-w) solid ${borderColor}`, borderRadius: "var(--radius-lg)",
            boxShadow: focus ? "var(--shadow-focus)" : "none", outline: "none",
            cursor: disabled ? "not-allowed" : "pointer", opacity: disabled ? 0.6 : 1,
            transition: "border-color var(--duration-fast) var(--ease-standard), box-shadow var(--duration-fast) var(--ease-standard)",
            ...style,
          }}
          {...rest}
        >
          {options ? options.map((o) => <option key={o.value} value={o.value}>{o.label}</option>) : children}
        </select>
        <i data-lucide="chevron-down" style={{ position: "absolute", right: 10, width: 16, height: 16, color: "var(--text-tertiary)", pointerEvents: "none" }} />
      </div>
      {(hint || error) && (
        <span style={{ fontSize: "var(--text-xs)", color: error ? "var(--danger-text)" : "var(--text-tertiary)" }}>
          {error || hint}
        </span>
      )}
    </div>
  );
}
