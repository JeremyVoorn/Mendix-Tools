import React from "react";

const sizeMap = {
  sm: { height: "var(--control-sm)", font: "var(--text-sm)", pad: "0 8px" },
  md: { height: "var(--control-md)", font: "var(--text-base)", pad: "0 10px" },
  lg: { height: "var(--control-lg)", font: "var(--text-md)", pad: "0 12px" },
};

/**
 * Text input with optional label, leading icon/prefix, error state, and mono mode
 * (for versions, connection strings, ports).
 */
export function Input({
  label,
  hint,
  error,
  size = "md",
  leftIcon,
  rightSlot,
  mono = false,
  disabled = false,
  id,
  style,
  containerStyle,
  ...rest
}) {
  const s = sizeMap[size] || sizeMap.md;
  const [focus, setFocus] = React.useState(false);
  const inputId = id || React.useId();
  const borderColor = error ? "var(--danger)" : focus ? "var(--accent)" : "var(--border-strong)";

  return (
    <div style={{ display: "flex", flexDirection: "column", gap: "6px", ...containerStyle }}>
      {label && (
        <label htmlFor={inputId} style={{ fontSize: "var(--text-sm)", fontWeight: "var(--fw-medium)", color: "var(--text-secondary)" }}>
          {label}
        </label>
      )}
      <div
        style={{
          display: "flex", alignItems: "center", gap: "8px",
          height: s.height, padding: s.pad,
          background: disabled ? "var(--bg-subtle)" : "var(--bg-surface)",
          border: `var(--border-w) solid ${borderColor}`,
          borderRadius: "var(--radius-lg)",
          boxShadow: focus ? "var(--shadow-focus)" : "none",
          transition: "border-color var(--duration-fast) var(--ease-standard), box-shadow var(--duration-fast) var(--ease-standard)",
          opacity: disabled ? 0.6 : 1,
        }}
      >
        {leftIcon && <span style={{ display: "inline-flex", color: "var(--text-tertiary)", flexShrink: 0 }}>{leftIcon}</span>}
        <input
          id={inputId}
          disabled={disabled}
          onFocus={(e) => { setFocus(true); rest.onFocus?.(e); }}
          onBlur={(e) => { setFocus(false); rest.onBlur?.(e); }}
          style={{
            flex: 1, minWidth: 0, border: "none", outline: "none", background: "transparent",
            color: "var(--text-primary)", fontSize: s.font,
            fontFamily: mono ? "var(--font-mono)" : "var(--font-sans)",
            ...style,
          }}
          {...rest}
        />
        {rightSlot && <span style={{ display: "inline-flex", flexShrink: 0 }}>{rightSlot}</span>}
      </div>
      {(hint || error) && (
        <span style={{ fontSize: "var(--text-xs)", color: error ? "var(--danger-text)" : "var(--text-tertiary)" }}>
          {error || hint}
        </span>
      )}
    </div>
  );
}
