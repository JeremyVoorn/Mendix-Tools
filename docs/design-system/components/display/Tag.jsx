import React from "react";

/**
 * Removable/selectable tag chip. Used for filters, selected environments,
 * branch/label chips. Set `onRemove` to show a dismiss ✕; `mono` for identifiers.
 */
export function Tag({ children, onRemove, icon, mono = false, tone = "neutral", style, ...rest }) {
  const tones = {
    neutral: { bg: "var(--bg-inset)", fg: "var(--text-secondary)", bd: "var(--border)" },
    accent: { bg: "var(--accent-subtle)", fg: "var(--accent-text)", bd: "var(--accent-subtle-border)" },
  };
  const t = tones[tone] || tones.neutral;
  return (
    <span style={{
      display: "inline-flex", alignItems: "center", gap: "6px", height: "24px",
      padding: onRemove ? "0 4px 0 9px" : "0 9px",
      fontSize: "var(--text-xs)", fontFamily: mono ? "var(--font-mono)" : "var(--font-sans)",
      color: t.fg, background: t.bg, border: `var(--border-w) solid ${t.bd}`,
      borderRadius: "var(--radius-md)", whiteSpace: "nowrap", ...style,
    }} {...rest}>
      {icon}
      {children}
      {onRemove && (
        <button type="button" onClick={onRemove} aria-label="Remove"
          style={{
            display: "inline-flex", alignItems: "center", justifyContent: "center",
            width: 16, height: 16, padding: 0, border: "none", borderRadius: "var(--radius-sm)",
            background: "transparent", color: "currentColor", cursor: "pointer", opacity: 0.6,
          }}
          onMouseEnter={(e) => (e.currentTarget.style.opacity = "1")}
          onMouseLeave={(e) => (e.currentTarget.style.opacity = "0.6")}
        >
          <i data-lucide="x" style={{ width: 12, height: 12 }} />
        </button>
      )}
    </span>
  );
}
