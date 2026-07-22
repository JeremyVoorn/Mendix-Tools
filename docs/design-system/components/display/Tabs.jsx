import React from "react";

/**
 * Horizontal tab bar. Controlled (`value`/`onChange`) or uncontrolled.
 * Underline style by default; use for switching views within a screen
 * (Overview / Backups / Logs). Tabs are [{value,label,icon,count}].
 */
export function Tabs({ tabs = [], value, defaultValue, onChange, style, ...rest }) {
  const [internal, setInternal] = React.useState(defaultValue ?? tabs[0]?.value);
  const active = value ?? internal;
  const select = (v) => { if (value === undefined) setInternal(v); onChange?.(v); };

  return (
    <div role="tablist" style={{
      display: "flex", alignItems: "stretch", gap: "2px",
      borderBottom: "var(--border-w) solid var(--border)", ...style,
    }} {...rest}>
      {tabs.map((t) => {
        const isActive = t.value === active;
        return (
          <button key={t.value} role="tab" aria-selected={isActive} type="button"
            onClick={() => select(t.value)}
            style={{
              position: "relative", display: "inline-flex", alignItems: "center", gap: "7px",
              padding: "9px 12px", marginBottom: "-1px", background: "transparent", border: "none",
              cursor: "pointer", fontSize: "var(--text-base)", fontFamily: "var(--font-sans)",
              fontWeight: isActive ? "var(--fw-semibold)" : "var(--fw-medium)",
              color: isActive ? "var(--text-primary)" : "var(--text-secondary)",
              borderBottom: `2px solid ${isActive ? "var(--accent)" : "transparent"}`,
              transition: "color var(--duration-fast) var(--ease-standard)",
            }}
            onMouseEnter={(e) => { if (!isActive) e.currentTarget.style.color = "var(--text-primary)"; }}
            onMouseLeave={(e) => { if (!isActive) e.currentTarget.style.color = "var(--text-secondary)"; }}
          >
            {t.icon}
            {t.label}
            {t.count != null && (
              <span style={{
                fontSize: "var(--text-2xs)", fontFamily: "var(--font-mono)",
                padding: "1px 6px", borderRadius: "var(--radius-full)",
                background: isActive ? "var(--accent-subtle)" : "var(--bg-inset)",
                color: isActive ? "var(--accent-text)" : "var(--text-tertiary)",
              }}>{t.count}</span>
            )}
          </button>
        );
      })}
    </div>
  );
}
