import React from "react";

const tones = {
  neutral: { bg: "var(--bg-inset)", fg: "var(--text-secondary)", bd: "var(--border)" },
  accent: { bg: "var(--accent-subtle)", fg: "var(--accent-text)", bd: "var(--accent-subtle-border)" },
  success: { bg: "var(--success-subtle)", fg: "var(--success-text)", bd: "transparent" },
  warning: { bg: "var(--warning-subtle)", fg: "var(--warning-text)", bd: "transparent" },
  danger: { bg: "var(--danger-subtle)", fg: "var(--danger-text)", bd: "transparent" },
  info: { bg: "var(--info-subtle)", fg: "var(--info-text)", bd: "transparent" },
  db: { bg: "var(--db-subtle)", fg: "var(--db-text)", bd: "transparent" },
  package: { bg: "var(--package-subtle)", fg: "var(--package-text)", bd: "transparent" },
};

/**
 * Compact status/label pill. Used for environment state (Running, Stopped),
 * deploy results, version tags, counts. `dot` prepends a status dot.
 */
export function Badge({ children, tone = "neutral", size = "md", dot = false, icon, style, ...rest }) {
  const t = tones[tone] || tones.neutral;
  const dims = size === "sm"
    ? { pad: "1px 7px", font: "var(--text-2xs)", h: "18px" }
    : { pad: "2px 9px", font: "var(--text-xs)", h: "22px" };
  return (
    <span style={{
      display: "inline-flex", alignItems: "center", gap: "5px", height: dims.h,
      padding: dims.pad, fontSize: dims.font, fontWeight: "var(--fw-medium)",
      fontFamily: "var(--font-sans)", lineHeight: 1, whiteSpace: "nowrap",
      color: t.fg, background: t.bg, border: `var(--border-w) solid ${t.bd}`,
      borderRadius: "var(--radius-full)", ...style,
    }} {...rest}>
      {dot && <span style={{ width: 6, height: 6, borderRadius: "50%", background: "currentColor" }} />}
      {icon}
      {children}
    </span>
  );
}
