import React from "react";

const dims = { sm: { w: 32, h: 18, k: 12 }, md: { w: 40, h: 22, k: 16 } };

/**
 * On/off switch for settings and instant toggles (auto-refresh, dark mode,
 * "keep local copy"). Prefer over Checkbox for immediate-effect options.
 */
export function Switch({ label, description, checked, defaultChecked, disabled = false, onChange, size = "md", id, ...rest }) {
  const sId = id || React.useId();
  const [internal, setInternal] = React.useState(defaultChecked || false);
  const isControlled = checked !== undefined;
  const on = isControlled ? checked : internal;
  const d = dims[size] || dims.md;

  const handle = (e) => { if (!isControlled) setInternal(e.target.checked); onChange?.(e); };

  return (
    <label htmlFor={sId} style={{ display: "inline-flex", alignItems: description ? "flex-start" : "center", gap: "10px", cursor: disabled ? "not-allowed" : "pointer", opacity: disabled ? 0.55 : 1 }}>
      <span style={{ position: "relative", display: "inline-flex", flexShrink: 0, marginTop: description ? "1px" : 0 }}>
        <input id={sId} type="checkbox" checked={on} onChange={handle} disabled={disabled}
          style={{ position: "absolute", opacity: 0, width: d.w, height: d.h, margin: 0, cursor: "inherit" }} {...rest} />
        <span style={{
          width: d.w, height: d.h, borderRadius: "var(--radius-full)",
          background: on ? "var(--accent)" : "var(--border-strong)",
          transition: "background var(--duration-normal) var(--ease-standard)",
          display: "inline-flex", alignItems: "center", padding: 2,
        }}>
          <span style={{
            width: d.k, height: d.k, borderRadius: "50%", background: "#fff",
            boxShadow: "var(--shadow-sm)",
            transform: on ? `translateX(${d.w - d.k - 4}px)` : "translateX(0)",
            transition: "transform var(--duration-normal) var(--ease-out)",
          }} />
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
